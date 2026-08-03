using System.Collections;
using System.Reflection;
using IoTPlatform.Infrastructure.Protocol.Adapters;
using IoTPlatform.Services;

namespace IoTPlatform.IntegrationTests.Infrastructure;

/// <summary>
/// 静态状态清理器（架构方案 §3.6）。
///
/// 【为什么必须有】
///   安圣链路上存在两处进程级静态字典，它们不随 DI 作用域、也不随 TestServer 重建而清空：
///     1. <c>AnShengMqttProtocolAdapter.DeviceKinds</c>      —— IMEI → 设备型号，影响指令目录校验；
///     2. <c>AnShengCommandService.FrameIdCommandIdMap</c>   —— frameId → commandId，影响回包关联。
///   若不清理，用例 A 注册的设备型号会泄漏给用例 B，制造「单跑绿、连跑红」的幽灵失败。
///
/// 【清理手段】
///   · DeviceKinds 已有公开的 <c>ClearDeviceKinds()</c>，直接调用（零反射，最稳）；
///   · FrameIdCommandIdMap 是 <c>private static readonly</c>，只能反射取字段再调其 <c>Clear()</c>。
///     反射失败时不抛异常、只记 <see cref="LastError"/> —— 生产代码重命名字段不应让全部用例爆红，
///     但会在 <see cref="Verify"/> 里被显式检出。
/// </summary>
public static class StaticStateResetter
{
    private const string FrameIdMapFieldName = "FrameIdCommandIdMap";

    /// <summary>最近一次清理中遇到的问题描述；一切正常时为 null。</summary>
    public static string? LastError { get; private set; }

    /// <summary>
    /// 清空全部已知的进程级静态状态。应在每个用例开始前调用（见 <c>IntegrationTestBase</c>）。
    /// </summary>
    public static void ResetAll()
    {
        LastError = null;

        ClearDeviceKinds();
        ClearFrameIdCommandIdMap();
    }

    /// <summary>
    /// 校验静态状态确实可被清理。建议在冒烟用例里断言其为 true，
    /// 这样生产代码一旦重命名字段就会立刻被发现，而不是悄悄退化成「没清」。
    /// </summary>
    public static bool Verify()
    {
        ResetAll();
        return LastError == null;
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
