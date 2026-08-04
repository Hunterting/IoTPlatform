namespace IoTPlatform.Configuration;

/// <summary>
/// 安圣事件管道参数。绑定配置节 <c>AnSheng:Event</c>。
///
/// 沿用 <see cref="AnShengProbeOptions"/> 的既有范式：常量 <c>SectionName</c> + 全部属性带默认值，
/// 这样即使配置文件里没有该节点，服务也能以「生产安全默认值」启动。
/// </summary>
public class AnShengEventOptions
{
    /// <summary>配置节名称。</summary>
    public const string SectionName = "AnSheng:Event";

    /// <summary>
    /// 遗嘱（<c>close</c>）离线去抖窗口，单位秒，默认 30（决策 3）。
    ///
    /// 【为什么要去抖】
    ///   4G 设备的 MQTT 连接会因基站切换/信号抖动而短暂断开，Broker 立刻投递遗嘱。
    ///   若收到 <c>close</c> 就置离线，页面上会出现大量「秒掉线秒上线」的噪声告警。
    ///   30 秒窗口内若收到 <c>connected</c> 则撤销离线，只有真掉线才落状态。
    ///
    /// 【测试环境请调小】集成测试把它设为 2，否则单个用例要等 30 秒。
    /// </summary>
    public int CloseDebounceSeconds { get; set; } = 30;

    /// <summary>
    /// <c>recv485</c> 是否写入事件溯源表，默认 <c>false</c>（决策 2）。
    ///
    /// 【为什么默认关】
    ///   D4 §372 明确「对 <c>recv485</c> 这类高频数据不写事件表」。485 透传帧可能以秒级频率上行，
    ///   写事件表会在数周内把表撑到千万行级别，而其业务价值高度依赖外接设备的寄存器语义
    ///   （485 专用表结构未定义，登记为待办 W4）。
    ///   数据本身不会丢——它仍然经 Normalizer → <c>IDataCollectionService</c> →
    ///   <c>DeviceDataRecord.SensorData</c>（longtext，无损承载十六进制帧）。
    ///
    /// 【什么时候打开】现场排障时临时置 <c>true</c>，可在事件表里按时间线看到完整 485 帧序列。
    /// 测试环境固定为 <c>true</c> 以便断言。
    /// </summary>
    public bool PersistRecv485 { get; set; }

    /// <summary>
    /// 是否抑制既有数据桥（<c>ProtocolConfigService → IDataCollectionService</c>）对事件报文的落库，
    /// 默认 <c>false</c>（决策 B-2）。
    ///
    /// 【为什么默认不抑制】
    ///   D4 §370 定的是「并存而非替换」。事件报文确实会在两条通路上各写一条
    ///   <c>DeviceDataRecord</c>（一条是事件归一化数据点，一条是原始报文快照），
    ///   但在 T6 阶段就改既有桥的落库行为，回归面（DeviceSensor 更新、DataRule 触发、
    ///   历史曲线连续性）远大于「少写一行记录」的收益。
    ///   等事件表数据被验证可信后，再评估是否开启。
    ///
    /// 【这是逃生开关，不是常规配置】开启前请确认没有 DataRule 依赖既有桥写出的原始快照。
    /// </summary>
    public bool SuppressLegacyDataBridge { get; set; }

    /// <summary>
    /// 事件表保留天数，默认 90。
    ///
    /// ⚠️ T6 只<b>声明</b>该配置项，<b>不实现</b>清理作业（登记为待办 W3，归 Phase 3）。
    /// 放在这里是为了让保留期策略有唯一出处，避免将来清理作业上线时又造一份配置。
    /// </summary>
    public int RetentionDays { get; set; } = 90;

    /// <summary>
    /// 在途命令条目的存活时长，单位秒，默认 30（决策 1）。
    ///
    /// 超过该时长的条目在下一次 <c>IsInFlight</c> / <c>CompleteAsync</c> 访问时被惰性摘除，
    /// 使对应的上行报文自然退化为 AutoReport 分支。
    /// T6 不挂后台清扫作业（那是 T7 增强点）。
    /// </summary>
    public int PendingTtlSeconds { get; set; } = 30;

    /// <summary>
    /// 管道解析不到租户时使用的兜底租户码，默认空串。
    ///
    /// 解析优先级见设计文档 §7.2：
    /// <c>Device.AppCode → AnShengDeviceProfile.AppCode → DiscoveredAnShengDevice.AppCode →
    /// 本配置项 → ""（并记 Warn）</c>。
    /// 单租户部署可在此填死租户码，避免后台线程写出 <c>AppCode=""</c> 的孤儿事件行。
    /// </summary>
    public string DefaultAppCode { get; set; } = string.Empty;

    /// <summary>
    /// 归一化后的 <c>CloseDebounceSeconds</c>：下限 1 秒，避免误配 0/负数导致
    /// <c>Task.Delay</c> 立即到期、去抖形同虚设。
    /// </summary>
    public int EffectiveCloseDebounceSeconds => CloseDebounceSeconds > 0 ? CloseDebounceSeconds : 1;

    /// <summary>
    /// 归一化后的 <c>PendingTtlSeconds</c>：下限 1 秒，避免误配导致条目刚注册就过期。
    /// </summary>
    public int EffectivePendingTtlSeconds => PendingTtlSeconds > 0 ? PendingTtlSeconds : 1;
}
