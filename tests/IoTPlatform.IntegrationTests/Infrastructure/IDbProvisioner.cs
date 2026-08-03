namespace IoTPlatform.IntegrationTests.Infrastructure;

/// <summary>
/// 测试数据库供给器 —— 本脚手架关于「数据库从哪来」的<b>唯一抽象点</b>。
///
/// 设计目的（架构方案 §1.2）：今天用真实 MySQL 上的一次性 schema，
/// 将来开发机/CI 装了 Docker，只需把 <see cref="DbProvisionerFactory"/> 选到
/// <see cref="TestcontainersDbProvisioner"/>，其余文件零改动。
/// </summary>
public interface IDbProvisioner : IAsyncDisposable
{
    /// <summary>
    /// 供给完成后的完整连接串（含 Database=一次性库名）。
    /// 在 <see cref="ProvisionAsync"/> 成功之前访问会抛 <see cref="InvalidOperationException"/>。
    /// </summary>
    string ConnectionString { get; }

    /// <summary>供给完成后的库（schema）名。未供给时为空串。</summary>
    string SchemaName { get; }

    /// <summary>
    /// 服务器版本串（如 <c>8.0.36</c>）。供 Pomelo 显式构造 <c>ServerVersion</c>，
    /// 避免每次建 DbContext 都做一次 <c>AutoDetect</c> 握手。取不到时为 null。
    /// </summary>
    string? ServerVersionString { get; }

    /// <summary>
    /// 创建一次性测试库并返回其连接串。可重复调用，第二次起直接返回已创建的连接串。
    /// </summary>
    /// <param name="ct">取消令牌。</param>
    /// <returns>完整连接串。</returns>
    Task<string> ProvisionAsync(CancellationToken ct = default);

    /// <summary>
    /// 销毁一次性测试库（DROP SCHEMA）。必须幂等、且在异常路径下也能安全调用。
    /// 设置环境变量 <c>IOT_TEST_KEEP_SCHEMA=1</c> 时跳过销毁，便于排障。
    /// </summary>
    /// <param name="ct">取消令牌。</param>
    Task DisposeSchemaAsync(CancellationToken ct = default);

    /// <summary>
    /// 额外创建一个「草稿库」，用于不能污染主测试库的场景（例如迁移 Down 回滚验证）。
    /// 调用方负责用 <see cref="DropScratchAsync"/> 清理。
    /// </summary>
    /// <param name="purpose">用途后缀，只允许字母数字下划线，会拼进库名便于识别。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>草稿库的完整连接串。</returns>
    Task<string> ProvisionScratchAsync(string purpose, CancellationToken ct = default);

    /// <summary>
    /// 销毁 <see cref="ProvisionScratchAsync"/> 创建的草稿库。
    /// </summary>
    /// <param name="scratchConnectionString">草稿库连接串。</param>
    /// <param name="ct">取消令牌。</param>
    Task DropScratchAsync(string scratchConnectionString, CancellationToken ct = default);
}
