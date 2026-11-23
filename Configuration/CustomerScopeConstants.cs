namespace IoTPlatform.Configuration;

/// <summary>
/// 多客户授权模型跨文件共享常量（设计 §7.2-1 / §5.2）。
///
/// <para>任何签发、解析、校验授权客户集合的代码都必须引用本类，
/// 不得硬编码 claim 名或上限数字——JwtHelper / CustomerScopeService / 前端三处共用同一契约。</para>
/// </summary>
public static class CustomerScopeConstants
{
    /// <summary>JWT 中承载授权客户集合的 claim 名（逗号分隔的 long 客户 ID，如 "1,2,4"）。</summary>
    public const string ClaimManagedCustomerIds = "ManagedCustomerIds";

    /// <summary>默认单个 admin 可管理的最大客户数（控制 JWT 体积与分配 API 边界）。</summary>
    public const int DefaultMaxManagedCustomers = 50;

    /// <summary>分配 API / 迁移校验使用的上限别名，语义等同 <see cref="DefaultMaxManagedCustomers"/>。</summary>
    public const int MaxManagedCustomersPerAdmin = DefaultMaxManagedCustomers;

    /// <summary>授权客户集合 claim 的分隔符。</summary>
    public const char ManagedCustomerIdsSeparator = ',';

    /// <summary>user_customers.Source：超管在分配 API 中人工授予。</summary>
    public const string SourceManual = "manual";

    /// <summary>user_customers.Source：迁移从 admin 旧单值列回填。</summary>
    public const string SourceMigration = "migration";

    /// <summary>user_customers.Source：种子数据回填。</summary>
    public const string SourceSeed = "seed";
}
