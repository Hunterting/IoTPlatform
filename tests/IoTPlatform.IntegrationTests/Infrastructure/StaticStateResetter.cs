using System.Collections;
using System.Reflection;
using IoTPlatform.Infrastructure.Protocol.Adapters;
using IoTPlatform.Services;
using IoTPlatform.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace IoTPlatform.IntegrationTests.Infrastructure;

/// <summary>
/// 静态状态清理器（架构方案 §3.6）。
///
/// 【为什么必须有】
///   安圣链路上存在五处跨用例存活的状态，它们不随 DI 作用域、也不随 TestServer 重建而清空：
///     1. <c>AnShengMqttProtocolAdapter.DeviceKinds</c>      —— IMEI → 设备型号，影响指令目录校验；
///     2. <c>AnShengCommandService.FrameIdCommandIdMap</c>   —— frameId → commandId，影响回包关联；
///     3. <c>AnShengProbeService</c> 的在途等待表             —— (imei, method) → 等待者，影响探测；
///     4. <c>AnShengOfflineDebouncer</c> 的在途去抖窗口       —— IMEI → CTS，影响 T6 验收 #5（★ 最毒）；
///     5. <c>IAnShengPendingCommandStore</c> 的在途命令表     —— (imei, frameId)，影响三分支路由判定。
///   若不清理，用例 A 注册的设备型号会泄漏给用例 B，制造「单跑绿、连跑红」的幽灵失败。
///
/// 【T6 新增的 4 / 5 为什么危险程度高于前三项】
///   去抖窗口是<b>带定时器的</b>：用例 A 投了 close 却在窗口到期前就结束，
///   那个 <c>Task.Delay</c> 仍在后台跑，到期后会调 <c>OnDeviceOfflineAsync</c>
///   把<b>用例 B 刚播种的同 IMEI 设备</b>改成 offline——B 的现场被一个已经结束的用例改写，
///   症状是「B 单跑绿、跟在 A 后面跑红」，且失败点在 B 里根本看不出成因。
///   在途命令表同理：A 遗留的 frameId 会让 B 的自动上报被判成 Response，直接改变路由分支。
///
/// 【清理手段】
///   · DeviceKinds 已有公开的 <c>ClearDeviceKinds()</c>，直接调用（零反射，最稳）；
///   · FrameIdCommandIdMap 是 <c>private static readonly</c>，只能反射取字段再调其 <c>Clear()</c>。
///     反射失败时不抛异常、只记 <see cref="LastError"/> —— 生产代码重命名字段不应让全部用例爆红，
///     但会在 <see cref="Verify"/> 里被显式检出；
///   · 探测在途表通过 <c>IAnShengProbeService.ClearPending()</c> 清（需要 DI 容器，故有 provider 重载）。
///
/// 【⚠ 绝对禁止：AnShengUplinkHub.Reset()】
///   直觉上「清静态状态」应该顺手把静态总线也 Reset 一下，但那是个陷阱：
///   <c>AnShengProbeService</c> 注册为 Singleton，<b>在构造时</b>订阅总线，而 TestServer 全程只有一个，
///   Singleton 不会被重建。一旦 Reset，订阅被连根拔起，此后<b>所有</b>用例的探测都会永久超时，
///   且症状是「第一个用例绿、后面全红」——极难定位。
///   正确的隔离粒度是清「在途等待」，不是清「订阅」。
/// </summary>
public static class StaticStateResetter
{
    private const string FrameIdMapFieldName = "FrameIdCommandIdMap";

    /// <summary>最近一次清理中遇到的问题描述；一切正常时为 null。</summary>
    public static string? LastError { get; private set; }

    /// <summary>
    /// 清空全部已知的进程级静态状态。应在每个用例开始前调用（见 <c>IntegrationTestBase</c>）。
    /// </summary>
    /// <param name="services">
    /// 根服务提供器。传入后会额外清理 <c>IAnShengProbeService</c> 的在途等待表；
    /// 传 null 时跳过该步（供不依赖 DI 的纯静态清理场景使用）。
    /// </param>
    public static void ResetAll(IServiceProvider? services = null)
    {
        LastError = null;

        ClearDeviceKinds();
        ClearFrameIdCommandIdMap();
        ClearProbePending(services);
        ClearOfflineDebouncer(services);
        ClearPendingCommands(services);
    }

    /// <summary>
    /// 校验静态状态确实可被清理。建议在冒烟用例里断言其为 true，
    /// 这样生产代码一旦重命名字段就会立刻被发现，而不是悄悄退化成「没清」。
    /// </summary>
    /// <param name="services">可选的根服务提供器，见 <see cref="ResetAll"/>。</param>
    public static bool Verify(IServiceProvider? services = null)
    {
        ResetAll(services);
        return LastError == null;
    }

    /// <summary>
    /// 清空探测服务的在途等待表。
    ///
    /// 【为什么不是可选的锦上添花】
    ///   用例 A 若在探测超时前就结束（比如断言完就返回），它留下的等待者仍挂在 Singleton 上。
    ///   用例 B 探测同一台设备的同一个方法时，<c>_pending.TryAdd</c> 会失败，
    ///   B 会莫名其妙地「一条指令都没发就探测失败」。
    /// </summary>
    /// <param name="services">根服务提供器；为 null 则跳过。</param>
    private static void ClearProbePending(IServiceProvider? services)
    {
        if (services == null)
        {
            return;
        }

        try
        {
            // 用 GetService 而非 GetRequiredService：探测服务未注册时应视为「无须清理」，
            // 而不是让整个测试基类在 InitializeAsync 阶段崩掉。
            var probeService = services.GetService<IAnShengProbeService>();
            probeService?.ClearPending();
        }
        catch (Exception ex)
        {
            Append($"清理 IAnShengProbeService 在途等待失败：{ex.Message}");
        }
    }

    /// <summary>
    /// 取消并清空全部在途离线去抖窗口（T6 决策 3）。
    ///
    /// 【为什么必须清，且必须在播种之前清】
    ///   <c>AnShengOfflineDebouncer</c> 是 Singleton，窗口靠 <c>Task.Delay</c> 计时。
    ///   用例 A 投了 <c>close</c> 之后若不等窗口到期就结束，定时器仍在后台运行；
    ///   等它到期时用例 B 已经开始，<c>OnDeviceOfflineAsync</c> 会把 B 播种的同 IMEI 设备
    ///   悄悄改成 offline。B 的「设备应在线」断言随即失败，而失败原因在 B 的代码里毫无痕迹。
    ///   <c>ClearAll()</c> 会 Cancel 每个 CTS，被取消的 <c>ScheduleOfflineAsync</c>
    ///   走 <c>OperationCanceledException</c> 分支静默退出，不会置离线。
    /// </summary>
    /// <param name="services">根服务提供器；为 null 则跳过。</param>
    private static void ClearOfflineDebouncer(IServiceProvider? services)
    {
        if (services == null)
        {
            return;
        }

        try
        {
            // GetService 而非 GetRequiredService：T6 未落地的分支上该 Singleton 可能未注册，
            // 「未注册」等价于「无须清理」，不应让整个测试基类在 InitializeAsync 阶段崩掉。
            var debouncer = services.GetService<AnShengOfflineDebouncer>();
            debouncer?.ClearAll();
        }
        catch (Exception ex)
        {
            Append($"清理 AnShengOfflineDebouncer 在途窗口失败：{ex.Message}");
        }
    }

    /// <summary>
    /// 清空在途命令表（frameId 登记簿）。
    ///
    /// 【为什么会改变路由判定】
    ///   <c>AnShengMessageRouter.Classify</c> 第 3 级用 <c>IsInFlight(imei, frameId)</c>
    ///   区分「设备自动上报」与「我方下发的应答」。用例 A 下发过命令却没等到回包，
    ///   条目会一直挂到 TTL 到期；用例 B 若恰好复用同一 frameId（测试里 frameId 常被固定），
    ///   B 的自动上报会被判成 Response，走完全不同的分支——这类失败极难归因。
    /// </summary>
    /// <param name="services">根服务提供器；为 null 则跳过。</param>
    private static void ClearPendingCommands(IServiceProvider? services)
    {
        if (services == null)
        {
            return;
        }

        try
        {
            var store = services.GetService<IAnShengPendingCommandStore>();
            store?.ClearAll();
        }
        catch (Exception ex)
        {
            Append($"清理 IAnShengPendingCommandStore 在途命令失败：{ex.Message}");
        }
    }

    private static void ClearDeviceKinds()
    {
        try
        {
            // 生产代码已提供公开清理入口，优先使用。
            AnShengMqttProtocolAdapter.ClearDeviceKinds();
        }
        catch (Exception ex)
        {
            Append($"清理 AnShengMqttProtocolAdapter.DeviceKinds 失败：{ex.Message}");
        }
    }

    private static void ClearFrameIdCommandIdMap()
    {
        try
        {
            var field = typeof(AnShengCommandService).GetField(
                FrameIdMapFieldName,
                BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);

            if (field == null)
            {
                Append(
                    $"未找到 AnShengCommandService.{FrameIdMapFieldName} 静态字段——" +
                    "生产代码可能已重命名，请同步更新 StaticStateResetter。");
                return;
            }

            var value = field.GetValue(null);
            if (value == null)
            {
                // 字段存在但为 null，等价于已清空。
                return;
            }

            // ConcurrentDictionary<,> 与 Dictionary<,> 都有无参 Clear()，按方法名反射调用即可，
            // 无需硬编码泛型参数，生产代码换值类型也不会失配。
            var clear = value.GetType().GetMethod("Clear", Type.EmptyTypes);
            if (clear == null)
            {
                Append($"AnShengCommandService.{FrameIdMapFieldName} 类型 {value.GetType().Name} 没有无参 Clear() 方法。");
                return;
            }

            clear.Invoke(value, null);

            // 反射调用后二次确认真的空了（IEnumerable 计数，避免依赖具体泛型）。
            if (value is IEnumerable enumerable && enumerable.GetEnumerator().MoveNext())
            {
                Append($"AnShengCommandService.{FrameIdMapFieldName} 调用 Clear() 后仍非空。");
            }
        }
        catch (Exception ex)
        {
            Append($"清理 AnShengCommandService.{FrameIdMapFieldName} 失败：{ex.Message}");
        }
    }

    private static void Append(string message)
    {
        LastError = LastError == null ? message : $"{LastError}{Environment.NewLine}{message}";
    }
}
