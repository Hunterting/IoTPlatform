using System;

namespace IoTPlatform.Configuration;

/// <summary>
/// 安圣水流量自动采集轮询参数。绑定配置节 <c>AnSheng:WaterFlow</c>。
///
/// 沿用 <see cref="AnShengCommandOptions"/> / <see cref="AnShengProbeOptions"/> 的既有范式：
/// 常量 <c>SectionName</c> + 全部属性带默认值，即使配置文件里没有该节点，
/// 轮询宿主也能以「生产安全默认值」启动。
///
/// 【为什么把间隔放配置而不是写死】
///   · 运维可按现场设备规模调频（几十台 vs 几千台），不用改代码发版；
///   · 集成测试可注入短间隔 / 直接置 <c>PollEnabled=false</c> 手工触发
///     <c>PollOnceAsync</c>，杜绝后台线程与断言竞态（同 SweepEnabled 的测试纪律）。
///
/// 【配置解析失败的行为】Options 绑定器对无法转换的值（如 "abc"）会<b>跳过赋值</b>，
/// 属性保持代码里的默认值 60 秒 —— 这就是「解析失败用默认值」的实现方式，
/// 不需要额外的 try/parse。
/// </summary>
public class AnShengWaterFlowOptions
{
    /// <summary>配置节名称。</summary>
    public const string SectionName = "AnSheng:WaterFlow";

    /// <summary>
    /// 是否启用后台轮询宿主，默认 <c>true</c>。
    ///
    /// 【集成测试请置 false】关掉后台线程、由用例手工触发一轮
    /// <c>AnShengWaterFlowPollingHostedService.PollOnceAsync</c>，
    /// 杜绝「后台线程与断言竞态」导致的随机红灯（测试纪律，同 SweepEnabled）。
    /// </summary>
    public bool PollEnabled { get; set; } = true;

    /// <summary>
    /// 轮询间隔，单位秒，默认 60。
    ///
    /// 每轮对所有已认领水开关设备下发一次 <c>getFlowInfo</c>。
    /// 设备侧是 4G 模组、且自带 30s 级 getDevStatus 自动上报，60s 一问已足够密；
    /// 更密的间隔对「累计流量」类物理量没有信息增益，只会耗流量。
    /// </summary>
    public int PollIntervalSeconds { get; set; } = 60;

    /// <summary>
    /// 启动后的初始延迟，单位秒，默认 20。
    ///
    /// 等协议适配器（AnShengMqttProtocolAdapter）拉起、MQTT 订阅就绪后再开始第一轮，
    /// 避免启动即打出一批必然失败的命令记录。
    /// </summary>
    public int InitialDelaySeconds { get; set; } = 20;

    /// <summary>
    /// 归一化后的轮询间隔：
    /// <list type="bullet">
    ///   <item>0 / 负数（误配）⇒ 回落默认 60 秒；</item>
    ///   <item>1 ~ 10 秒 ⇒ 钳到 10 秒下限，防止误配打爆设备（4G 模组按流量计费，
    ///     1 秒一问等于每台设备每天 8.6 万条下行）；</item>
    ///   <item>其余按原值生效。</item>
    /// </list>
    /// </summary>
    public int EffectivePollIntervalSeconds => PollIntervalSeconds switch
    {
        <= 0 => 60,
        <= 10 => 10,
        _ => PollIntervalSeconds
    };

    /// <summary>归一化后的初始延迟：负数按 0 处理（立即开始第一轮）。</summary>
    public int EffectiveInitialDelaySeconds => InitialDelaySeconds >= 0 ? InitialDelaySeconds : 0;
}
