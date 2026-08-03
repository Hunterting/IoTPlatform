namespace IoTPlatform.IntegrationTests.Infrastructure;

/// <summary>
/// 【占位实现】基于 Testcontainers 的数据库供给器。
///
/// 现状（架构方案 §1.3 实测）：本机<b>未安装 Docker / Podman / WSL</b>，
/// <c>Testcontainers.MySql</c> 当前跑不起来，因此本类只保留接口形状并抛出带指引的异常。
///
/// 启用步骤（Docker 就位后，其余文件零改动）：
///   1. 在 <c>IoTPlatform.IntegrationTests.csproj</c> 解开 <c>Testcontainers.MySql</c> 的 PackageReference 注释；
///   2. 在同一 PropertyGroup 追加 <c>&lt;DefineConstants&gt;$(DefineConstants);TESTCONTAINERS&lt;/DefineConstants&gt;</c>；
///   3. 把下方 <c>#if TESTCONTAINERS</c> 区块内的实现补全（示例代码见 README「切换到 Testcontainers」一节）；
///   4. 设置环境变量 <c>IOT_TEST_DB_PROVIDER=testcontainers</c>。
/// </summary>
public sealed class TestcontainersDbProvisioner : IDbProvisioner
{
#if TESTCONTAINERS
    // Docker 就位后在此处引入 Testcontainers.MySql：
    //   private readonly MySqlContainer _container = new MySqlBuilder()
    //       .WithImage("mysql:8.0.36")
    //       .WithDatabase("iot_platform_test")
    //       .WithUsername("root")
    //       .WithPassword("root")
    //       .Build();
    // 并在 ProvisionAsync 中 StartAsync()、返回 _container.GetConnectionString()。
#endif

    private const string NotEnabledMessage =
        "TestcontainersDbProvisioner 尚未启用：本机未安装 Docker（架构方案 §1.3）。" +
        "请改用默认的 MySqlDbProvisioner（不要设置 IOT_TEST_DB_PROVIDER，或设为 mysql），" +
        "或按本类顶部注释的 4 个步骤启用 Testcontainers。";

    /// <inheritdoc />
    public string ConnectionString => throw new NotSupportedException(NotEnabledMessage);

    /// <inheritdoc />
    public string SchemaName => string.Empty;

    /// <inheritdoc />
    public string? ServerVersionString => null;

    /// <inheritdoc />
    public Task<string> ProvisionAsync(CancellationToken ct = default)
        => throw new NotSupportedException(NotEnabledMessage);

    /// <inheritdoc />
    public Task<string> ProvisionScratchAsync(string purpose, CancellationToken ct = default)
        => throw new NotSupportedException(NotEnabledMessage);

    /// <inheritdoc />
    public Task DropScratchAsync(string scratchConnectionString, CancellationToken ct = default)
        => throw new NotSupportedException(NotEnabledMessage);

    /// <inheritdoc />
    public Task DisposeSchemaAsync(CancellationToken ct = default) => Task.CompletedTask;

    /// <inheritdoc />
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

/// <summary>
/// 供给器选择器 —— 「切换数据库策略」的<b>唯一一行改动点</b>。
/// </summary>
public static class DbProvisionerFactory
{
    /// <summary>
    /// 依据环境变量 <c>IOT_TEST_DB_PROVIDER</c> 创建供给器。
    /// </summary>
    /// <returns>默认 <see cref="MySqlDbProvisioner"/>；值为 <c>testcontainers</c> 时返回 <see cref="TestcontainersDbProvisioner"/>。</returns>
    public static IDbProvisioner Create()
    {
        var provider = Environment.GetEnvironmentVariable(SharedTestConstants.EnvVars.DbProvider);

        return provider?.Trim().ToLowerInvariant() switch
        {
            "testcontainers" or "docker" => new TestcontainersDbProvisioner(),
            _ => new MySqlDbProvisioner()
        };
    }
}
