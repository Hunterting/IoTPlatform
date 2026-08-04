using System;
using System.Collections.Generic;

namespace IoTPlatform.Configuration;

/// <summary>
/// 安圣命令服务参数。绑定配置节 <c>AnSheng:Command</c>。
///
/// 沿用 <see cref="AnShengEventOptions"/> / <see cref="AnShengProbeOptions"/> 的既有范式：
/// 常量 <c>SectionName</c> + 全部属性带默认值，即使配置文件里没有该节点，服务也能以
/// 「生产安全默认值」启动。
///
/// 【为什么这些时间常量不能硬编码】（决策 D4）
///   验收 #5 要验「30 秒无应答 → Timeout」。若 30s 写死在代码里，测试就只能<b>真等 30 秒</b>。
///   把 TTL 参数化后，集成测试注入 200ms 即可在 1 秒内跑完整条超时链路；
///   而「默认就是 30 秒」这个语义由 <see cref="ResolveTtl"/> 的单元测试守护。
///   这也是本期<b>不引入</b> <c>Microsoft.Extensions.TimeProvider.Testing</c> 的底气所在。
/// </summary>
public class AnShengCommandOptions
{
    /// <summary>配置节名称。</summary>
    public const string SectionName = "AnSheng:Command";

    /// <summary>
    /// 普通命令的应答等待时长，单位秒，默认 30。
    ///
    /// 超过该时长仍未收到设备应答，命令由旁路清扫置为
    /// <c>AnShengCommandStatus.Timeout</c>，并从在途表摘除。
    /// </summary>
    public int DefaultTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// 慢命令的应答等待时长，单位秒，默认 60。
    ///
    /// 适用于 <see cref="LongRunningMethods"/> 中列出的方法 —— 它们需要设备侧读取/聚合大量数据，
    /// 用 30 秒会把「正常但慢」误判成超时。
    /// </summary>
    public int LongRunningTimeoutSeconds { get; set; } = 60;

    /// <summary>
    /// 旁路清扫周期，单位秒，默认 5。
    ///
    /// 【为什么是秒级而不是分钟级】命令超时是秒级语义。这也是决策 D8 否决
    /// 「并入 <c>AnShengOfflineDebouncer</c>」的核心理由 —— 离线防抖是分钟级周期，
    /// 合并后要么离线判定变敏感，要么命令超时被拖慢到分钟级。
    /// </summary>
    public int SweepIntervalSeconds { get; set; } = 5;

    /// <summary>
    /// 是否启用后台清扫宿主，默认 <c>true</c>。
    ///
    /// 【集成测试请置 false】关掉后台线程、由用例<b>手工触发</b>一轮清扫，
    /// 杜绝「后台线程与断言竞态」导致的随机红灯（测试纪律 §9.5-2）。
    /// </summary>
    public bool SweepEnabled { get; set; } = true;

    /// <summary>
    /// 品类未知（无 <c>AnShengDeviceProfile</c> 或 <c>Kind == Unknown</c>）时是否拒绝下发，默认 <c>false</c>。
    ///
    /// 【为什么默认放行】（决策 D7）
    ///   <c>AnShengCommandSpec.IsSupportedBy(Unknown) == true</c> 是 T7 之前就存在的安全阀。
    ///   存量设备大多还没上报过 <c>deviceInfo</c>、没有档案，上线当天若按 Unknown 一律拒绝，
    ///   等于把现网全部老设备打死。T7 是重构，<b>不得改变存量设备的可用性</b>。
    ///
    /// 【什么时候打开】运维确认 Profile 覆盖率达标后，切 <c>true</c> 进入严格模式。
    /// </summary>
    public bool RejectWhenKindUnknown { get; set; }

    /// <summary>
    /// 命令记录保留天数，默认 90。
    ///
    /// ⚠️ T7 只<b>声明</b>该配置项，<b>不实现</b>清理作业（开放问题 U1，建议归 T16 运维专项）。
    /// 放在这里是为了让保留期策略有唯一出处，避免将来清理作业上线时又造一份配置。
    /// </summary>
    public int RecordRetentionDays { get; set; } = 90;

    /// <summary>
    /// 使用 <see cref="LongRunningTimeoutSeconds"/> 的慢命令白名单。
    ///
    /// 【为什么用集合而不是写死 if】将来 T10/T11 若发现别的慢命令，改配置即可，不用改代码。
    /// 大小写不敏感 —— 安圣 method 全是 camelCase，但调用方偶有笔误。
    /// </summary>
    public HashSet<string> LongRunningMethods { get; set; } =
        new(StringComparer.OrdinalIgnoreCase) { "getLogs", "getEMStatistics" };

    /// <summary>
    /// 延时任务「写后回读」的等待毫秒数，默认 120（T8 设计 §7.4）。
    ///
    /// 【为什么要等这一小会儿】<c>startDelayTask</c> / <c>stopDelayTask</c> 刚出网，
    ///   设备还没来得及把新状态落到自己的任务表；此刻立刻问 <c>getDelayTasks</c>，
    ///   拿回来的极可能是<b>改之前</b>的旧值，反而把平台的乐观镜像覆盖成陈旧数据。
    ///   ≥100ms 是设计给出的下限（同时满足 R3 节流），120 是留了余量的默认值。
    ///
    /// 【集成测试请置 0】关掉等待可让回读在同一轮断言内完成，避免 sleep 式测试。
    /// </summary>
    public int ReadbackDelayMs { get; set; } = 120;

    /// <summary>
    /// 延时任务镜像的陈旧阈值，单位小时，默认 24（T8 设计 §7.5）。
    ///
    /// 设备是权威、平台只存快照，因此镜像「旧」是常态而非故障。
    /// 超过该阈值仍未被任何应答刷新过，才在 <c>AnShengDelayTaskDto.IsStale</c> 上打标，
    /// 提示前端「这份数据可能已经跟设备对不上，建议手动同步」。
    /// </summary>
    public int MirrorStaleHours { get; set; } = 24;

    /// <summary>
    /// 归一化后的 <see cref="ReadbackDelayMs"/>：负数按默认 120 处理（0 合法，表示不等待）。
    /// </summary>
    public int EffectiveReadbackDelayMs => ReadbackDelayMs >= 0 ? ReadbackDelayMs : 120;

    /// <summary>
    /// 归一化后的镜像陈旧阈值：非正数按默认 24 小时处理，避免误配 0 导致所有镜像恒为陈旧。
    /// </summary>
    public TimeSpan EffectiveMirrorStaleThreshold =>
        TimeSpan.FromHours(MirrorStaleHours > 0 ? MirrorStaleHours : 24);

    /// <summary>归一化后的 <see cref="DefaultTimeoutSeconds"/>：下限 1 秒，避免误配 0/负数导致命令刚发就超时。</summary>
    public int EffectiveDefaultTimeoutSeconds => DefaultTimeoutSeconds > 0 ? DefaultTimeoutSeconds : 1;

    /// <summary>归一化后的 <see cref="LongRunningTimeoutSeconds"/>：下限 1 秒。</summary>
    public int EffectiveLongRunningTimeoutSeconds => LongRunningTimeoutSeconds > 0 ? LongRunningTimeoutSeconds : 1;

    /// <summary>归一化后的 <see cref="SweepIntervalSeconds"/>：下限 1 秒，避免 <c>PeriodicTimer</c> 构造抛异常。</summary>
    public int EffectiveSweepIntervalSeconds => SweepIntervalSeconds > 0 ? SweepIntervalSeconds : 1;

    /// <summary>
    /// 解析某个 method 的应答等待时长。
    ///
    /// 【这是「30 秒」这个数字的唯一出处】单元测试 <c>Options_ResolveTtl_Defaults</c> 断言
    /// <c>ResolveTtl("action") == 30s</c>、<c>ResolveTtl("getLogs") == 60s</c>、
    /// <c>ResolveTtl("getEMStatistics") == 60s</c>，从而在<b>不真等 30 秒</b>的前提下守护规格语义。
    /// </summary>
    /// <param name="method">安圣协议 method；null / 空串按普通命令处理。</param>
    /// <returns>该命令的 TTL。</returns>
    public TimeSpan ResolveTtl(string? method)
    {
        if (!string.IsNullOrWhiteSpace(method) && LongRunningMethods.Contains(method))
        {
            return TimeSpan.FromSeconds(EffectiveLongRunningTimeoutSeconds);
        }

        return TimeSpan.FromSeconds(EffectiveDefaultTimeoutSeconds);
    }
}
