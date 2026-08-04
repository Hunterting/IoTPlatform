using System;
using System.ComponentModel.DataAnnotations;
using IoTPlatform.Data;

namespace IoTPlatform.Models;

/// <summary>
/// 安圣命令生命周期状态机（T7 决策 D2）。
///
/// 【状态流转】
/// <code>
///   Pending ──(下发成功)──► Sent ──(设备应答 ok)────► Succeeded
///      │                     │  └(设备应答 err)────► Failed
///      │                     └──(旁路清扫超时)─────► Timeout
///      └──(Guard 拒绝 / 下发异常)──────────────────► Rejected / Failed
/// </code>
///
/// 【终态只有四种】<see cref="Succeeded"/> / <see cref="Failed"/> / <see cref="Timeout"/> / <see cref="Rejected"/>。
///   「不存在永远 Pending」是 T7 §7 的硬约定 —— 运维只要看 <c>Status IN (Pending, Sent)</c>
///   且 <c>TimeoutAt &lt; now</c> 就能判断链路是不是卡住了。
///
/// 【为什么以 int 落库】沿用 <see cref="AnShengEventKind"/> 的既有范式：
///   MySQL 5.7.26 下不用原生 <c>ENUM</c>（增值要 <c>ALTER TABLE</c> 锁表，
///   且 Pomelo 对 ENUM 的映射跨版本不稳定）。
///
/// 【枚举值一经发布只能追加，不得重排】数值即数据库里的存量数据，改序号等于改历史。
/// </summary>
public enum AnShengCommandStatus
{
    /// <summary>已受理、已落库，但尚未发布到 MQTT。</summary>
    Pending = 0,

    /// <summary>已发布到 MQTT，等待设备应答（在途）。</summary>
    Sent = 1,

    /// <summary>设备明确应答成功。</summary>
    Succeeded = 2,

    /// <summary>设备明确应答失败，或发布过程本身抛异常。</summary>
    Failed = 3,

    /// <summary>超过 TTL 仍无应答，由 <c>AnShengCommandSweepHostedService</c> 旁路置终态。</summary>
    Timeout = 4,

    /// <summary>被 <c>AnShengCommandGuard</c> 拒绝，<b>未出网</b>（零 MQTT 发布、零在途登记、零 frameId）。</summary>
    Rejected = 5
}

/// <summary>
/// 命令被拒绝的原因（仅当 <see cref="AnShengCommandStatus.Rejected"/> 时有值）。
///
/// 【为什么要独立成列而不是塞进 ErrorMessage】
///   验收 #1/#2/#3 要求断言「拒绝的**类别**」。文案随时会改，机器可读的枚举才是稳定契约；
///   前端也需要按类别决定提示方式（品类不支持 → 灰掉按钮；固件不足 → 引导升级）。
///
/// 【枚举值只能追加】同 <see cref="AnShengCommandStatus"/>。
/// </summary>
public enum AnShengCommandRejectReason
{
    /// <summary>设备品类不支持该命令（验收 #1）。</summary>
    RejectedByKind = 0,

    /// <summary>参数校验不通过，含 slotNum 越界（验收 #2）。</summary>
    RejectedByValidation = 1,

    /// <summary>设备固件版本低于参数/命令的最低门槛（验收 #3，决策 D5 选「拦截」）。</summary>
    RejectedByFirmware = 2,

    /// <summary>设备离线或适配器未连接，下发必然失败，提前短路。</summary>
    RejectedByOffline = 3,

    /// <summary>协议目录中不存在该 method，或该 method 是设备上报事件（平台不可下发）。</summary>
    RejectedByUnknownMethod = 4,

    /// <summary>高危命令缺少二次确认（为 T13 <c>setMqtt</c> 预留，T7 恒放行）。</summary>
    RejectedByConfirm = 5
}

/// <summary>
/// 安圣命令记录表（T7 决策 D2：19 列 + 5 索引）。
///
/// 【定位】它是「平台下发命令」的<b>生命周期流水</b>，与 T6 的 <see cref="AnShengDeviceEvent"/>
///   （设备上行事件溯源）职责互补、互不重叠。
///   因此本表<b>没有</b> <c>Direction</c> 列 —— 恒为 Downlink 的列既浪费存储，
///   又会制造「两张表都能查上行」的错觉。
///
/// 【终态由谁写 —— 一条不变式】
///   任一时刻一条记录的终态<b>只由一个组件</b>写入，互斥点是在途表
///   <c>ConcurrentDictionary.TryRemove</c> 的 CAS 语义：谁先摘除成功谁写终态。
/// <code>
///   AnShengCommandService              → Pending / Sent / Rejected（受理路径）
///   AnShengMessageRouter (Scoped)      → Succeeded / Failed        （应答路径）
///   AnShengCommandSweepHostedService   → Timeout                   （超时路径）
/// </code>
///
/// 【MySQL 5.7.26 兼容】
///   · 两个枚举列一律 <c>int</c>（<c>HasConversion&lt;int&gt;()</c>），禁用原生 ENUM；
///   · 不使用 CHECK 约束（5.7 静默忽略，制造「以为有校验」的假象），校验放应用层；
///   · <see cref="RequestJson"/> / <see cref="ResponseJson"/> 用 <c>longtext</c>，只存不查、不进索引；
///   · <see cref="DurationMs"/> <b>存值而非生成列</b> —— 5.7 的生成列不能进函数索引，
///     且 Pomelo 对生成列的迁移支持并不稳；
///   · 时间列统一 <c>datetime(6)</c> 存 UTC，禁 <c>timestamp</c>（2038 问题 + 时区隐式转换）。
///
/// 【多租户 ★ 最容易踩】
///   本类实现 <see cref="IHasAppCode"/>，会被 <c>AppDbContext.ConfigureGlobalQueryFilters</c>
///   自动追加 <c>WHERE AppCode = @current</c>。但超时清扫跑在 <c>BackgroundService</c> 线程，
///   <c>ITenantContextAccessor.Current</c> 为 null ⇒ 过滤器<b>不生效</b>。
///   故：写入路径必须<b>显式赋值</b> <see cref="AppCode"/>；后台更新路径必须
///   <c>IgnoreQueryFilters()</c> 并<b>按主键</b> <see cref="Id"/> 定位。
///
/// 【与 DeviceCommand 的关系】<see cref="CommandId"/> 与 <c>DeviceCommand.CommandId</c> <b>同值</b>，
///   由 <c>DeviceCommandService</c> 透传。这是取代 R1 静态 <c>FrameIdCommandIdMap</c> 的持久化软关联，
///   进程重启不丢。
/// </summary>
public class AnShengCommandRecord : IHasAppCode
{
    /// <summary><see cref="ErrorMessage"/> 的列长上限，供写入方截断时复用。</summary>
    public const int ErrorMessageMaxLength = 512;

    /// <summary>
    /// <see cref="RequestJson"/> / <see cref="ResponseJson"/> 的落库长度上限（16 KB）。
    ///
    /// 风险 R3：<c>getLogs</c> 这类命令的应答可达数百 KB，全量落 <c>longtext</c> 会迅速撑大表。
    /// 超长内容由写入方截断并追加 <see cref="TruncationMarker"/>，保证「能看出被截断了」。
    /// </summary>
    public const int JsonMaxLength = 16 * 1024;

    /// <summary>JSON 截断标记。出现它即表示原文比落库内容更长。</summary>
    public const string TruncationMarker = "...[truncated]";

    /// <summary>内部主键。</summary>
    public long Id { get; set; }

    /// <summary>
    /// 租户码。后台线程（清扫宿主）写入路径上<b>必须显式赋值</b>，EF 不会替你填。
    /// </summary>
    [Required]
    [MaxLength(50)]
    public string AppCode { get; set; } = string.Empty;

    /// <summary>
    /// 对外稳定标识（GUID 字符串），<b>唯一索引</b>。
    ///
    /// 与 <c>DeviceCommand.CommandId</c> 同值，两表软关联，避免双份真相；
    /// <c>GET /api/v1/ansheng/commands/{commandId}</c> 按它查询。
    /// 唯一约束同时是天然的幂等护栏 —— 同一 CommandId 重复提交会在 DB 层直接失败。
    /// </summary>
    [Required]
    [MaxLength(36)]
    public string CommandId { get; set; } = string.Empty;

    /// <summary>
    /// 平台设备主键；<b>可空</b>。
    ///
    /// 未认领设备（产线探测阶段）同样允许按 IMEI 下发命令（开放问题 U5 的默认假设：允许），
    /// 此时没有 Device 行，故本列可空。
    /// </summary>
    public long? DeviceId { get; set; }

    /// <summary>设备 IMEI。未认领时这是<b>唯一</b>可用标识。</summary>
    [Required]
    [MaxLength(32)]
    public string Imei { get; set; } = string.Empty;

    /// <summary>安圣协议 method，保真存储（不做归一化，便于与设备日志逐字比对）。</summary>
    [Required]
    [MaxLength(32)]
    public string Method { get; set; } = string.Empty;

    /// <summary>
    /// 帧 ID；<b>可空</b> —— 拒绝态的命令根本没走到生成 frameId 这一步。
    /// 「<see cref="FrameId"/> IS NULL」与「<see cref="SentAt"/> IS NULL」共同构成验收 #1
    /// 「MQTT 零发布」的持久化证据。
    /// </summary>
    [MaxLength(64)]
    public string? FrameId { get; set; }

    /// <summary>命令状态（int 落库）。</summary>
    public AnShengCommandStatus Status { get; set; } = AnShengCommandStatus.Pending;

    /// <summary>拒绝原因（int 落库）；仅 <see cref="AnShengCommandStatus.Rejected"/> 时有值。</summary>
    public AnShengCommandRejectReason? RejectReason { get; set; }

    /// <summary>
    /// <b>掩码后</b>的下发参数 JSON（决策 D3）。
    ///
    /// ⚠️ 这里存的永远是副本，<c>setMqtt.password</c> 等 <c>IsSecret</c> 参数已被替换为 <c>"***"</c>。
    /// 实际发布到 MQTT 的报文仍是明文 —— 两者<b>不共享字典实例</b>。
    /// </summary>
    [Required]
    public string RequestJson { get; set; } = string.Empty;

    /// <summary><b>掩码后</b>的设备应答原文；未收到应答（Pending/Sent/Timeout/Rejected）时为 null。</summary>
    public string? ResponseJson { get; set; }

    /// <summary>
    /// 机器可读错误码。取值来源：设备回包的 <c>result</c> 字段，
    /// 或平台内部错误码（如 <c>TIMEOUT</c> / <c>ADAPTER_OFFLINE</c> / <c>PUBLISH_FAILED</c>）。
    /// </summary>
    [MaxLength(64)]
    public string? ErrorCode { get; set; }

    /// <summary>面向人的失败原因（已截断至 <see cref="ErrorMessageMaxLength"/>）。校验失败时存 Errors 的拼接。</summary>
    [MaxLength(ErrorMessageMaxLength)]
    public string? ErrorMessage { get; set; }

    /// <summary>受理时刻（UTC）。命令一旦进入 Service 就有这个时间，拒绝态也有。</summary>
    public DateTime IssuedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 实际发布到 MQTT 的时刻（UTC）；<b>拒绝态为 null</b>。
    /// <b>这是验收 #1「MQTT 无任何发布」的持久化证据列。</b>
    /// </summary>
    public DateTime? SentAt { get; set; }

    /// <summary>终态时刻（UTC）。Succeeded / Failed / Timeout / Rejected 四种终态都会写。</summary>
    public DateTime? CompletedAt { get; set; }

    /// <summary>
    /// 超时判定时刻（UTC），<c>= SentAt + TTL</c>；未发布则为 null。
    /// 旁路清扫按 <c>(Status, TimeoutAt)</c> 索引扫描「未完成且已超时」。
    /// </summary>
    public DateTime? TimeoutAt { get; set; }

    /// <summary>
    /// 往返耗时（毫秒）<c>= CompletedAt - SentAt</c>。
    /// <b>写入时算好存值</b>，不用生成列（MySQL 5.7.26 红线）。
    /// </summary>
    public int? DurationMs { get; set; }

    /// <summary>操作者用户 ID（审计）。HTTP 路径取自 claim；后台/自动路径为 null。</summary>
    public long? OperatorUserId { get; set; }

    /// <summary>
    /// 把过长的 JSON 截断到 <see cref="JsonMaxLength"/> 并追加 <see cref="TruncationMarker"/>（风险 R3）。
    ///
    /// 放在实体上是为了让「截断口径」只有一个权威定义 —— Service / Router / SweepHost 三处写入方共用。
    /// </summary>
    /// <param name="json">原始 JSON，可为 null。</param>
    /// <returns>不超过 <see cref="JsonMaxLength"/> 的字符串；入参为 null 时原样返回 null。</returns>
    public static string? TruncateJson(string? json)
    {
        if (json is null || json.Length <= JsonMaxLength)
        {
            return json;
        }

        // 先切到「上限 - 标记长度」再拼标记，保证结果总长恰好不超过上限。
        var keep = JsonMaxLength - TruncationMarker.Length;
        return string.Concat(json.AsSpan(0, keep), TruncationMarker);
    }

    /// <summary>
    /// 把过长的错误文案截断到 <see cref="ErrorMessageMaxLength"/>。
    /// </summary>
    /// <param name="message">原始文案，可为 null。</param>
    /// <returns>不超过列长上限的字符串；入参为 null 时原样返回 null。</returns>
    public static string? TruncateErrorMessage(string? message)
    {
        if (message is null || message.Length <= ErrorMessageMaxLength)
        {
            return message;
        }

        return message[..ErrorMessageMaxLength];
    }
}
