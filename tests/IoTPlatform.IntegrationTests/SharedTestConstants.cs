namespace IoTPlatform.IntegrationTests;

/// <summary>
/// 集成测试跨文件共享常量。
///
/// 约定来源：架构方案《安圣二开设备 · 集成测试脚手架技术方案》§7「共享知识」。
/// 任何新增用例都必须复用这里的常量，禁止各写各的字面量。
/// </summary>
public static class SharedTestConstants
{
    /// <summary>
    /// 全测试统一租户码。
    ///
    /// 【硬约束】一个 <c>TestWebAppFactory</c> 实例只能对应一个 AppCode——
    /// EF 的模型缓存键不含 AppCode，全局租户过滤器会在首次建模时被「冻结」
    /// （见方案 §1.6①、§8-待明确 7）。需要验证租户隔离的用例必须另起 Factory。
    /// </summary>
    public const string AppCode = "TEST";

    /// <summary>测试用主设备 IMEI。与协议层 <c>AnShengLegacyWhitelistTests</c> 同值，便于两层日志/报文比对。</summary>
    public const string Imei = "864536072949900";

    /// <summary>测试用第二台设备 IMEI（多设备串扰类用例，如 T7 验收 4）。</summary>
    public const string SecondaryImei = "864536072949901";

    /// <summary>
    /// 名义上的安圣协议配置 Id。
    ///
    /// 注意：真实 Id 由 MySQL 自增列生成，播种后请用 <c>SeedResult.ProtocolConfigId</c>；
    /// 本常量仅用于「不依赖具体 Id」的场景（<c>FakeProtocolAdapterFactory</c> 对任意 configId 都返回默认替身）。
    /// </summary>
    public const int ProtocolConfigId = 9001;

    /// <summary>安圣协议类型标识，与 <c>ProtocolAdapterFactory</c> 的 switch 分支一致。</summary>
    public const string ProtocolTypeAnSheng = "ANSHENG_MQTT";

    /// <summary>上行主题前缀：设备 → 平台。</summary>
    public const string UplinkTopicPrefix = "/iot/server/iot-board/";

    /// <summary>下行主题前缀：平台 → 设备。</summary>
    public const string DownlinkTopicPrefix = "/iot/client/iot-board/";

    /// <summary>默认租户名称。</summary>
    public const string CustomerName = "集成测试租户";

    /// <summary>默认租户编码（<c>customers.Code</c> 唯一索引）。</summary>
    public const string CustomerCode = "TEST";

    /// <summary>默认管理员角色码，对应 <c>IoTPlatform.Configuration.Roles.ADMIN</c>。</summary>
    public const string RoleAdmin = "admin";

    /// <summary>超级管理员角色码，对应 <c>IoTPlatform.Configuration.Roles.SUPER_ADMIN</c>。</summary>
    public const string RoleSuperAdmin = "super_admin";

    /// <summary>默认测试用户 Id（落成 <c>ClaimTypes.NameIdentifier</c>）。</summary>
    public const string DefaultUserId = "1001";

    /// <summary>默认测试租户主键（落成 <c>CustomerId</c> claim）。</summary>
    public const string DefaultCustomerId = "1";

    /// <summary>xUnit 集合名。所有集成用例统一挂 <c>[Collection(SharedTestConstants.CollectionName)]</c>。</summary>
    public const string CollectionName = "Integration";

    /// <summary>
    /// <c>TestAuthHandler</c> 消费的请求头名称。
    /// 只要出现其中任意一个，请求即被视为「已认证」；一个都不带 ⇒ 匿名（用于 401 用例）。
    /// </summary>
    public static class Headers
    {
        /// <summary>用户主键 → <c>ClaimTypes.NameIdentifier</c>。</summary>
        public const string UserId = "X-Test-UserId";

        /// <summary>角色码 → <c>ClaimTypes.Role</c>，被 <c>PermissionAuthorizeAttribute</c> 消费。</summary>
        public const string Role = "X-Test-Role";

        /// <summary>租户码 → <c>AppCode</c> claim，被控制器与租户上下文消费。</summary>
        public const string AppCode = "X-Test-AppCode";

        /// <summary>租户主键 → <c>CustomerId</c> claim。</summary>
        public const string CustomerId = "X-Test-CustomerId";

        /// <summary>用户名 → <c>ClaimTypes.Name</c>。</summary>
        public const string UserName = "X-Test-UserName";

        /// <summary>全部测试头，供 <c>AuthTestHelper.AsAnonymous()</c> 一次性清除。</summary>
        public static readonly string[] All =
        {
            UserId, Role, AppCode, CustomerId, UserName
        };
    }

    /// <summary>
    /// 环境变量名集合。
    /// </summary>
    public static class EnvVars
    {
        /// <summary>测试 MySQL 服务器连接串（不含 Database，或 Database 会被忽略）。</summary>
        public const string MySqlConnection = "IOT_TEST_MYSQL";

        /// <summary>数据库供给方式：<c>mysql</c>（默认）或 <c>testcontainers</c>。</summary>
        public const string DbProvider = "IOT_TEST_DB_PROVIDER";

        /// <summary>置为 <c>1</c> 时保留测试 schema 不删除（排障用）。</summary>
        public const string KeepSchema = "IOT_TEST_KEEP_SCHEMA";

        /// <summary>
        /// 陈旧测试库回收阈值（小时），默认 <c>2</c>；置 <c>0</c> 关闭回收。
        /// 用于清理「进程被强杀导致 DisposeAsync 未执行」而残留的测试库。
        /// </summary>
        public const string SweepHours = "IOT_TEST_SWEEP_HOURS";
    }
}
