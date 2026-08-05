using IoTPlatform.Infrastructure.Protocol.AnSheng;
using IoTPlatform.Models;

namespace IoTPlatform.DTOs.Responses;

/// <summary>
/// 安圣命令下发响应
/// </summary>
public class AnShengCommandResponse
{
    /// <summary>是否成功</summary>
    public bool Success { get; set; }

    /// <summary>
    /// 平台命令标识（GUID 字符串），与 <c>AnShengCommandRecord.CommandId</c> /
    /// <c>DeviceCommand.CommandId</c> <b>同值</b>（T7 决策 D2/D6）。
    ///
    /// 【为什么它比 FrameId 更该被调用方记住】FrameId 是设备侧的帧序号，
    /// 拒绝态命令<b>根本没有</b> FrameId（未出网）；而 CommandId 从命令被受理那一刻就存在，
    /// 是 <c>GET /api/v1/ansheng/commands/{commandId}</c> 的查询键。
    /// </summary>
    public string? CommandId { get; set; }

    /// <summary>安圣 FrameId（请求-响应关联）。<b>被 Guard 拒绝时为 null</b>——未出网就没有帧。</summary>
    public string? FrameId { get; set; }

    /// <summary>下发的命令 JSON（用于调试）</summary>
    public string? Payload { get; set; }

    /// <summary>
    /// 机器可读的拒绝原因；仅当命令被 <c>AnShengCommandGuard</c> 拦下（未出网）时有值。
    ///
    /// 【前端/测试请断言它而不是 <see cref="ErrorMessage"/>】文案随时会改，枚举是稳定契约。
    /// 验收 #1/#2/#3 分别断言 <c>RejectedByKind</c> / <c>RejectedByValidation</c> / <c>RejectedByFirmware</c>。
    /// </summary>
    public AnShengCommandRejectReason? RejectReason { get; set; }

    /// <summary>
    /// 逐条校验错误。<see cref="ErrorMessage"/> 是它们的「；」拼接版，供直接展示；
    /// 需要逐条渲染（如表单字段级提示）时用本字段。永不为 null。
    /// </summary>
    public IReadOnlyList<string> Errors { get; set; } = Array.Empty<string>();

    /// <summary>
    /// 满足本次下发所需的<b>最低固件版本</b>；仅
    /// <see cref="AnShengCommandRejectReason.RejectedByFirmware"/> 时有值（决策 D5）。
    /// 前端据此提示「请先升级到 x.y.z 再试」。
    /// </summary>
    public string? RequiredFirmware { get; set; }

    /// <summary>错误信息（面向人的描述，勿用于程序分支判断）</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>下发时间</summary>
    public DateTime SentAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// 安圣设备发现项
/// </summary>
public class DiscoveredAnShengDeviceDto
{
    /// <summary>ID</summary>
    public long Id { get; set; }

    /// <summary>IMEI</summary>
    public string Imei { get; set; } = string.Empty;

    /// <summary>设备型号</summary>
    public string? Model { get; set; }

    /// <summary>网络类型</summary>
    public string? NetType { get; set; }

    /// <summary>首次发现时间</summary>
    public DateTime DiscoveredAt { get; set; }

    /// <summary>最后在线时间</summary>
    public DateTime? LastSeenAt { get; set; }

    /// <summary>是否已认领</summary>
    public bool IsClaimed { get; set; }

    /// <summary>认领后设备 ID</summary>
    public long? ClaimedDeviceId { get; set; }

    // ── T5 新增：待认领阶段的能力快照，供列表页在"点认领之前"就展示设备是什么 ──

    /// <summary>已判定的设备品类（枚举值）。</summary>
    public AnShengDeviceKind Kind { get; set; } = AnShengDeviceKind.Unknown;

    /// <summary>品类中文名，如 <c>4G开关</c>；未判定为 <c>未知品类</c>。</summary>
    public string KindName { get; set; } = AnShengDeviceKind.Unknown.ToDisplayName();

    /// <summary>
    /// 建议品类 —— 基于当前已知信息实时推断的结果，仅供前端做默认选中项。
    /// 与 <see cref="Kind"/> 的区别：<see cref="Kind"/> 是已落库的结论，本字段是"猜"。
    /// </summary>
    public AnShengDeviceKind SuggestedKind { get; set; } = AnShengDeviceKind.Unknown;

    /// <summary>建议品类的中文名，供前端直接渲染，免得再维护一份枚举→中文的映射。</summary>
    public string SuggestedKindName { get; set; } = AnShengDeviceKind.Unknown.ToDisplayName();

    /// <summary>插槽数量；未知为 <c>null</c>。</summary>
    public int? SlotAmount { get; set; }

    /// <summary>固件版本号。</summary>
    public string? Version { get; set; }

    /// <summary>物联卡 ICCID。</summary>
    public string? Iccid { get; set; }

    /// <summary>探测状态（枚举值）。</summary>
    public AnShengProbeStatus ProbeStatus { get; set; } = AnShengProbeStatus.NotProbed;

    /// <summary>探测失败原因摘要；成功或未探测为 <c>null</c>。</summary>
    public string? ProbeError { get; set; }

    /// <summary>最近一次探测时间（UTC）。</summary>
    public DateTime? LastProbedAt { get; set; }
}

/// <summary>
/// 安圣设备认领响应。
///
/// 【错误表达约定】
///   本平台业务失败仍返回 HTTP 200，靠 <c>ApiResponse.Code</c> 表达状态。
///   而<b>具体是哪一类失败</b>由 <see cref="ErrorCode"/> 这个机器可读常量承载，
///   <c>ApiResponse.Message</c> 只面向人。前端与测试<b>必须</b>断言 <see cref="ErrorCode"/>，
///   断言 Message 文案会在每次改文案时假红。
/// </summary>
public class ClaimAnShengDeviceResponse
{
    /// <summary>认领是否成功</summary>
    public bool Success { get; set; }

    /// <summary>创建后的设备 ID</summary>
    public long? DeviceId { get; set; }

    /// <summary>设备名称</summary>
    public string? DeviceName { get; set; }

    /// <summary>
    /// 机器可读错误码，取值见 <c>AnShengClaimErrorCodes</c>。成功时为 <c>null</c>。
    /// </summary>
    public string? ErrorCode { get; set; }

    /// <summary>错误信息（面向人的描述，勿用于程序分支判断）</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>最终落档的设备品类。</summary>
    public AnShengDeviceKind Kind { get; set; } = AnShengDeviceKind.Unknown;

    /// <summary>品类中文名，同时也是写入 <c>Device.Category</c> 的值。</summary>
    public string? KindName { get; set; }

    /// <summary>能力档案主键；认领失败时为 <c>null</c>。</summary>
    public long? ProfileId { get; set; }

    /// <summary>本次认领的探测状态。</summary>
    public AnShengProbeStatus ProbeStatus { get; set; } = AnShengProbeStatus.NotProbed;
}

/// <summary>
/// 安圣设备发现列表分页响应
/// </summary>
public class DiscoveredDeviceListResponse
{
    /// <summary>设备列表</summary>
    public List<DiscoveredAnShengDeviceDto> Items { get; set; } = new();

    /// <summary>总记录数</summary>
    public int Total { get; set; }

    /// <summary>当前页码</summary>
    public int Page { get; set; }

    /// <summary>每页条数</summary>
    public int PageSize { get; set; }
}

/// <summary>
/// 命令目录中单个参数规格的只读视图（T7-5 暴露给前端）。
///
/// 【安全】<see cref="IsSecret"/> 只暴露布尔标志，<b>绝不</b>暴露任何机密值；
/// 机密值既不会进本 DTO，也只在 <c>AnShengCommandRecord</c> 落库时以 "***" 形式存在。
/// </summary>
public class AnShengParamSpecDto
{
    /// <summary>参数名（JSON key），大小写敏感。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>JSON 类型（String / Int / Double / Bool / Array / Object）。</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>是否必填。</summary>
    public bool Required { get; set; }

    /// <summary>该参数要求的最低固件版本；为 null 表示无限制。</summary>
    public string? MinFirmware { get; set; }

    /// <summary>允许的枚举值；为 null 表示不做枚举校验。</summary>
    public IReadOnlyList<string>? AllowedValues { get; set; }

    /// <summary>数值下限（含）；仅对 Int/Double 生效。</summary>
    public double? Minimum { get; set; }

    /// <summary>数值上限（含）；仅对 Int/Double 生效。</summary>
    public double? Maximum { get; set; }

    /// <summary>本参数自身是否为敏感值（只暴露布尔标志，绝不暴露值）。</summary>
    public bool IsSecret { get; set; }
}

/// <summary>
/// 安圣命令目录条目（T7-5 只读 API <c>GET /catalog</c> 的返回项）。
///
/// 【设计契约】
///   · <c>method</c> / <c>supportedKinds</c> / <c>params</c> 为必需字段；
///   · <c>supportedKinds</c> 是<b>设备品类</b>名称数组（如 "Switch4G"），便于前端按设备过滤；
///   · 事件方法（connected / keyEvent / ...）必须带 <c>isEvent=true</c>，
///     前端据此区分「可下发」与「只会上行」。
/// </summary>
public class AnShengCommandSpecDto
{
    /// <summary>协议方法名，如 <c>getDevStatus</c>。</summary>
    public string Method { get; set; } = string.Empty;

    /// <summary>能力分组标签（通用命令 / 开关动作 / 定时任务 / 对时物联卡 等）。</summary>
    public string Group { get; set; } = string.Empty;

    /// <summary>中文标题。</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>是否为设备主动上报事件（平台不可下发）。</summary>
    public bool IsEvent { get; set; }

    /// <summary>协议文档是否标注为「测试中」。</summary>
    public bool IsBeta { get; set; }

    /// <summary>整条命令要求的最低固件版本；为 null 表示无限制。</summary>
    public string? MinFirmware { get; set; }

    /// <summary>支持该命令的设备品类名称数组（如 ["Switch4G", "SwitchWiFi"]）。</summary>
    public IReadOnlyList<string> SupportedKinds { get; set; } = Array.Empty<string>();

    /// <summary>命令参数规格列表（序列化键为 <c>params</c>，与验收契约一致）。</summary>
    public IReadOnlyList<AnShengParamSpecDto> Params { get; set; } = Array.Empty<AnShengParamSpecDto>();
}

/// <summary>
/// 单条命令记录的只读视图（T7-5 <c>GET /commands/{commandId}</c>）。
///
/// 【安全红线】<see cref="RequestJson"/> / <see cref="ResponseJson"/> 在落库时即已掩码，
/// 本 DTO 再经 <c>AnShengSecretMasker</c> 二次掩码，<b>永不</b>返回明文口令（T7 决策 D3）。
/// </summary>
public class AnShengCommandRecordDto
{
    /// <summary>平台命令标识（与 <c>DeviceCommand.CommandId</c> 同值）。</summary>
    public string CommandId { get; set; } = string.Empty;

    /// <summary>关联设备主键；未认领下发时为 null。</summary>
    public long? DeviceId { get; set; }

    /// <summary>设备 IMEI。</summary>
    public string Imei { get; set; } = string.Empty;

    /// <summary>安圣协议 method。</summary>
    public string Method { get; set; } = string.Empty;

    /// <summary>帧 ID；拒绝态为 null。</summary>
    public string? FrameId { get; set; }

    /// <summary>命令状态。</summary>
    public AnShengCommandStatus Status { get; set; }

    /// <summary>拒绝原因；仅 Rejected 态有值。</summary>
    public AnShengCommandRejectReason? RejectReason { get; set; }

    /// <summary>掩码后的下发参数 JSON。</summary>
    public string? RequestJson { get; set; }

    /// <summary>掩码后的设备应答 JSON；未收到应答时为 null。</summary>
    public string? ResponseJson { get; set; }

    /// <summary>机器可读错误码。</summary>
    public string? ErrorCode { get; set; }

    /// <summary>面向人的失败原因（已截断）。</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>受理时刻（UTC）。</summary>
    public DateTime IssuedAt { get; set; }

    /// <summary>实际发布时刻（UTC）；拒绝态为 null。</summary>
    public DateTime? SentAt { get; set; }

    /// <summary>终态时刻（UTC）。</summary>
    public DateTime? CompletedAt { get; set; }

    /// <summary>超时判定时刻（UTC）。</summary>
    public DateTime? TimeoutAt { get; set; }

    /// <summary>往返耗时（毫秒）。</summary>
    public int? DurationMs { get; set; }
}

/// <summary>
/// 安圣设备能力档案的只读视图（T7-5 <c>GET /{deviceId}/profile</c>）。
/// </summary>
public class AnShengDeviceProfileDto
{
    /// <summary>档案主键。</summary>
    public long Id { get; set; }

    /// <summary>设备 IMEI。</summary>
    public string Imei { get; set; } = string.Empty;

    /// <summary>关联正式设备主键；认领前为 null。</summary>
    public long? DeviceId { get; set; }

    /// <summary>设备品类。</summary>
    public AnShengDeviceKind Kind { get; set; }

    /// <summary>品类中文名。</summary>
    public string KindName { get; set; } = string.Empty;

    /// <summary>品类来源。</summary>
    public AnShengKindSource KindSource { get; set; }

    /// <summary>联网类型（4G / WiFi）。</summary>
    public string? NetType { get; set; }

    /// <summary>插槽数量。</summary>
    public int? SlotAmount { get; set; }

    /// <summary>相位数量。</summary>
    public int? PhaseAmount { get; set; }

    /// <summary>固件版本号。</summary>
    public string? Version { get; set; }

    /// <summary>模组型号。</summary>
    public string? Model { get; set; }

    /// <summary>物联卡 ICCID。</summary>
    public string? Iccid { get; set; }

    /// <summary>信号强度 1-31。</summary>
    public int? Signal { get; set; }

    /// <summary>最近一次探测状态。</summary>
    public AnShengProbeStatus ProbeStatus { get; set; }

    /// <summary>探测失败原因摘要。</summary>
    public string? ProbeError { get; set; }

    /// <summary>最近一次探测时间（UTC）。</summary>
    public DateTime? LastProbedAt { get; set; }

    /// <summary>
    /// 最近一次插槽状态快照（0=关 1=开），由设备应答写回（T8）。
    /// 设备权威 + 异步刷新，可能为 <c>null</c>（尚未收到过任何带 <c>slots[]</c> 的应答）。
    /// </summary>
    public int[]? Slots { get; set; }

    /// <summary><see cref="Slots"/> 写入时间（UTC）；未写回为 <c>null</c>。</summary>
    public DateTime? SlotsSnapshotAt { get; set; }
}

/// <summary>
/// 开关动作下发结果（T8 <c>action</c> / <c>actions</c> 端点）。
///
/// 包裹 <see cref="AnShengCommandResponse"/> 的关键字段，并附带<b>当前</b>插槽快照（来自
/// <c>Profile.SlotsSnapshot</c>，可能尚未反映本次下发结果——设备应答异步写回）。
/// </summary>
public class AnShengSwitchResultDto
{
    /// <summary>平台是否受理并下发了命令（被 Guard 拒收时为 false）。</summary>
    public bool Accepted { get; set; }

    /// <summary>平台命令标识（GUID），被拒时为 null。</summary>
    public string? CommandId { get; set; }

    /// <summary>安圣 FrameId，被拒（未出网）时为 null。</summary>
    public string? FrameId { get; set; }

    /// <summary>
    /// 机器可读拒绝原因；仅当命令被 <see cref="AnShengCommandGuard"/> 拦下时有值。
    /// 验收 #5 断言点：喇叭类设备为 <see cref="AnShengCommandRejectReason.RejectedByKind"/>。
    /// </summary>
    public AnShengCommandRejectReason? RejectReason { get; set; }

    /// <summary>面向人的失败原因（勿用于程序分支判断）。</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// 实际出网的 JSON 报文回显（被拒时为 <c>null</c>——因为压根没构造出网帧）。
    ///
    /// 【为什么响应体里要带原始报文】验收 #1 / #2 要断言「报文与协议文档字节级一致」
    /// （<c>action</c> 的字段序、<c>actions</c> 的 <c>slotNums</c> 必须是数组而非逗号串）。
    /// 若只回 <c>CommandId</c>，测试就得反查 <c>AnShengCommandRecord.RequestJson</c>——
    /// 而那份是<b>掩码后</b>的，字节级断言在它上面不成立。
    /// </summary>
    public string? Payload { get; set; }

    /// <summary>
    /// 当前插槽状态快照（0=关 1=开），来自 <c>Profile.SlotsSnapshot</c>；
    /// 设备应答尚未到达时可能为 <c>null</c> 或陈旧值。
    /// </summary>
    public int[]? Slots { get; set; }
}

/// <summary>
/// 单个延时任务镜像的只读视图（T8 <c>getDelayTasks</c> 端点）。
/// </summary>
public class AnShengDelayTaskDto
{
    /// <summary>插槽编号，从 1 开始。</summary>
    public int SlotNum { get; set; }

    /// <summary>是否启用。</summary>
    public bool Enable { get; set; }

    /// <summary>开始动作（on/off/toggle/none）。</summary>
    public string SAction { get; set; } = "none";

    /// <summary>结束动作（on/off/toggle）。</summary>
    public string EAction { get; set; } = "off";

    /// <summary>延时秒数。</summary>
    public int Secs { get; set; }

    /// <summary>任务计数快照（非实时）。</summary>
    public int Cnt { get; set; }

    /// <summary>镜像最后与设备同步的时刻（UTC）。</summary>
    public DateTime SyncedAt { get; set; }

    /// <summary>
    /// 是否陈旧：<c>(UtcNow - SyncedAt) &gt; 24h</c>。设备权威 + 只读镜像，超时才标陈旧。
    /// </summary>
    public bool IsStale { get; set; }
}

/// <summary>
/// 延时任务下发结果（T8 <c>startDelayTask</c> / <c>stopDelayTask</c> 端点）。
///
/// <see cref="Tasks"/> 为<b>乐观镜像快照</b>——命令一发出立即返回平台已有镜像，
/// 真实的设备镜像由写后回读（getDelayTasks 应答经 Router 钩子）异步覆盖并 bump <see cref="AnShengDelayTask.SyncedAt"/>。
/// </summary>
public class AnShengDelayTaskResultDto
{
    /// <summary>平台是否受理并下发了命令。</summary>
    public bool Accepted { get; set; }

    /// <summary>平台命令标识（GUID），被拒时为 null。</summary>
    public string? CommandId { get; set; }

    /// <summary>安圣 FrameId，被拒时为 null。</summary>
    public string? FrameId { get; set; }

    /// <summary>机器可读拒绝原因；喇叭类设备为 <see cref="AnShengCommandRejectReason.RejectedByKind"/>。</summary>
    public AnShengCommandRejectReason? RejectReason { get; set; }

    /// <summary>面向人的失败原因。</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>乐观镜像快照（立即返回，可能尚未反映本次下发）。</summary>
    public List<AnShengDelayTaskDto>? Tasks { get; set; }
}

// ─────────────────────────────────────────────────────────────
// T10 定时任务响应
// ─────────────────────────────────────────────────────────────

/// <summary>
/// 单个定时任务镜像的只读视图（T10 <c>GET /time-tasks</c> 端点）。
///
/// <see cref="TaskKind"/> 以字符串出网（<c>"Normal"</c> / <c>"Loop"</c>，全局注册
/// <c>JsonStringEnumConverter</c>），前端拿到可读值而非魔数。
/// </summary>
public class AnShengTimeTaskDto
{
    /// <summary>插槽编号，从 1 开始。</summary>
    public int SlotNum { get; set; }

    /// <summary>任务类型：普通 / 循环。</summary>
    public AnShengTimeTaskKind TaskKind { get; set; } = AnShengTimeTaskKind.Normal;

    /// <summary>同插槽同类型内序号，从 1 开始。</summary>
    public int TaskIndex { get; set; }

    /// <summary>设备分配的任务 id。</summary>
    public string? TaskId { get; set; }

    /// <summary>是否启用。</summary>
    public bool Enable { get; set; }

    /// <summary>每周生效的星期几（1-7）。</summary>
    public IReadOnlyList<int> WeekDays { get; set; } = Array.Empty<int>();

    /// <summary>【普通定时】动作小时（0-23）。</summary>
    public int Hour { get; set; }

    /// <summary>【普通定时】动作分钟（0-59）。</summary>
    public int Minute { get; set; }

    /// <summary>【普通定时】动作：<c>on</c> / <c>off</c> / <c>toggle</c>。</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>【普通定时】是否上报。</summary>
    public bool UploadEnable { get; set; }

    /// <summary>【循环定时】开始小时。</summary>
    public int SHour { get; set; }

    /// <summary>【循环定时】开始分钟。</summary>
    public int SMinute { get; set; }

    /// <summary>【循环定时】结束小时。</summary>
    public int EHour { get; set; }

    /// <summary>【循环定时】结束分钟。</summary>
    public int EMinute { get; set; }

    /// <summary>【循环定时】打开分钟数。</summary>
    public int OnMins { get; set; }

    /// <summary>【循环定时】关闭分钟数。</summary>
    public int OffMins { get; set; }

    /// <summary>镜像最后与设备同步的时刻（UTC）。</summary>
    public DateTime SyncedAt { get; set; }

    /// <summary>是否陈旧：<c>(UtcNow - SyncedAt) &gt; 24h</c>。</summary>
    public bool IsStale { get; set; }

    /// <summary>
    /// 乐观并发令牌。客户端下发前应回传该值；若与服务器当前值不一致返回 409（验收 #5）。
    /// </summary>
    public long RowVersion { get; set; }
}

/// <summary>单插槽定时任务集合只读视图。</summary>
public class AnShengSlotTimeTaskSetDto
{
    /// <summary>插槽编号，从 1 开始。</summary>
    public int SlotNum { get; set; }

    /// <summary>普通定时任务列表。</summary>
    public List<AnShengTimeTaskDto> TimeTasks { get; set; } = new();

    /// <summary>循环定时任务列表。</summary>
    public List<AnShengTimeTaskDto> LoopTimeTasks { get; set; } = new();
}

/// <summary>
/// 定时任务下发结果（T10 <c>setTimeTasks</c> / <c>setSlotTimeTasks</c> 端点）。
///
/// <see cref="Slots"/> 为<b>乐观镜像快照</b>——命令一发出立即返回平台已有镜像，真实设备镜像由
/// 写后回读（getTimeTasks / getSlotTimeTasks 应答经 Router 钩子）异步覆盖并 bump <see cref="AnShengTimeTask.SyncedAt"/>。
/// </summary>
public class AnShengTimeTaskResultDto
{
    /// <summary>平台是否受理并下发了命令。</summary>
    public bool Accepted { get; set; }

    /// <summary>平台命令标识（GUID），被拒时为 null。</summary>
    public string? CommandId { get; set; }

    /// <summary>安圣 FrameId，被拒时为 null。</summary>
    public string? FrameId { get; set; }

    /// <summary>机器可读拒绝原因；喇叭类设备为 <see cref="AnShengCommandRejectReason.RejectedByKind"/>。</summary>
    public AnShengCommandRejectReason? RejectReason { get; set; }

    /// <summary>面向人的失败原因。</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>实际出网 JSON 报文回显（被拒时为 null）。</summary>
    public string? Payload { get; set; }

    /// <summary>
    /// 是否因乐观并发冲突被拒（验收 #5）。<c>true</c> 时调用方应刷新镜像后重试；
    /// 此时 HTTP 状态码为 409（区别于业务拒绝的 200 + Code=400）。
    /// </summary>
    public bool ConcurrencyConflict { get; set; }

    /// <summary>乐观镜像快照（立即返回，可能尚未反映本次下发）。</summary>
    public List<AnShengSlotTimeTaskSetDto>? Slots { get; set; }
}

// ─────────────────────────────────────────────────────────────
// T11 电量计（实时 / 统计 / 校准）响应
// ─────────────────────────────────────────────────────────────

/// <summary>
/// 电量计命令下发结果（T11 全部写端点共用）。
///
/// 【为什么不带「乐观镜像」】与 T8/T10 不同，电量计是<b>只读采集</b>语义：
///   平台没有资格替设备猜一个电量值。统计行只在设备应答真的回来时才写
///   （Router 钩子 → <c>ApplyStatisticsReadbackAsync</c>），因此这里只回下发受理情况。
///   前端拿到 <see cref="Accepted"/> 后应轮询 <c>GET /energy/statistics</c> 取真值。
/// </summary>
public class AnShengEnergyResultDto
{
    /// <summary>平台是否受理并下发了命令。</summary>
    public bool Accepted { get; set; }

    /// <summary>平台命令标识（GUID），被拒时为 null。</summary>
    public string? CommandId { get; set; }

    /// <summary>安圣 FrameId，被拒时为 null（未出网就没有帧）。</summary>
    public string? FrameId { get; set; }

    /// <summary>
    /// 机器可读拒绝原因。喇叭类设备下发校准 / 统计命令时为
    /// <see cref="AnShengCommandRejectReason.RejectedByKind"/>（验收 #6 的断言点）。
    /// </summary>
    public AnShengCommandRejectReason? RejectReason { get; set; }

    /// <summary>面向人的失败原因。</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>实际出网 JSON 报文回显（被拒时为 null）。</summary>
    public string? Payload { get; set; }
}

/// <summary>
/// 电量计统计聚合行的只读视图（T11 <c>GET /energy/statistics</c>）。
///
/// <see cref="Granularity"/> 以字符串出网（<c>"Total"</c> / <c>"HourSum"</c> / <c>"Hour"</c> /
/// <c>"Day"</c> / <c>"Month"</c>，全局注册 <c>JsonStringEnumConverter</c>）。
/// </summary>
public class AnShengEmStatisticDto
{
    /// <summary>插槽编号，从 1 开始。</summary>
    public int SlotNum { get; set; }

    /// <summary>统计粒度。</summary>
    public AnShengEmGranularity Granularity { get; set; } = AnShengEmGranularity.Total;

    /// <summary>
    /// 周期键：<c>total</c> / <c>00:00</c>~<c>23:30</c> / <c>yyyyMMddHHmm</c> / <c>yyyyMMdd</c> / <c>yyyyMM</c>。
    /// </summary>
    public string PeriodKey { get; set; } = string.Empty;

    /// <summary>累计电量（kWh）。</summary>
    public double Kwh { get; set; }

    /// <summary>本行最后一次被设备应答刷新的时刻（UTC）。</summary>
    public DateTime SyncedAt { get; set; }

    /// <summary>是否陈旧：<c>(UtcNow - SyncedAt) &gt; 24h</c>。设备权威 + 平台只累积，超时才标陈旧。</summary>
    public bool IsStale { get; set; }
}
