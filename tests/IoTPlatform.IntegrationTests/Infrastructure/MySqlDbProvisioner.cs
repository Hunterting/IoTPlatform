using System.Globalization;
using System.Text.RegularExpressions;
using MySqlConnector;

namespace IoTPlatform.IntegrationTests.Infrastructure;

/// <summary>
/// 主方案供给器：在<b>真实 MySQL</b> 上创建一次性专用 schema。
///
/// 为什么是真实 MySQL 而不是 InMemory / SQLite（架构方案 §1.2）：
///   · 主机启动即 <c>Database.Migrate()</c>，InMemory 会抛关系型方法异常；
///   · 迁移本身是 T5/T6/T7/T10/T11 的验收对象；
///   · <c>json</c> 列、<c>utf8mb4</c>、唯一索引、事务、<c>RowVersion</c> 语义必须与生产一致。
///
/// 安全边界：库名固定前缀 <c>iot_platform_test_</c> + 时间戳 + 随机后缀，
/// 且 <see cref="DisposeSchemaAsync"/> 只允许 DROP 带该前缀的库，绝无可能误删业务库。
/// </summary>
public sealed class MySqlDbProvisioner : IDbProvisioner
{
    /// <summary>一次性测试库统一前缀，<see cref="DisposeSchemaAsync"/> 的安全护栏依赖它。</summary>
    public const string SchemaPrefix = "iot_platform_test_";

    /// <summary>
    /// 陈旧测试库回收阈值默认值（小时）。
    /// 取 2h 是为了在「及时回收」与「不误伤正在跑的另一次测试」之间留足余量。
    /// </summary>
    private const double DefaultSweepHours = 2;

    private static readonly Regex SafeIdentifier = new("^[A-Za-z0-9_]+$", RegexOptions.Compiled);

    private readonly string _serverConnectionString;
    private string _schemaName = string.Empty;
    private string _connectionString = string.Empty;
    private string? _serverVersionString;
    private bool _disposed;

    /// <summary>
    /// 用显式服务器连接串构造。
    /// </summary>
    /// <param name="serverConnectionString">不含 Database 的服务器级连接串。</param>
    public MySqlDbProvisioner(string serverConnectionString)
    {
        if (string.IsNullOrWhiteSpace(serverConnectionString))
        {
            throw new ArgumentException("服务器连接串不能为空", nameof(serverConnectionString));
        }

        _serverConnectionString = serverConnectionString;
    }

    /// <summary>
    /// 用 <see cref="TestConnectionStringResolver"/> 的解析结果构造（常规用法）。
    /// </summary>
    public MySqlDbProvisioner()
        : this(TestConnectionStringResolver.ResolveServerConnectionString())
    {
    }

    /// <inheritdoc />
    public string ConnectionString => string.IsNullOrEmpty(_connectionString)
        ? throw new InvalidOperationException("尚未调用 ProvisionAsync()，连接串不可用")
        : _connectionString;

    /// <inheritdoc />
    public string SchemaName => _schemaName;

    /// <inheritdoc />
    public string? ServerVersionString => _serverVersionString;

    /// <summary>服务器级连接串（不含 Database），仅供诊断输出使用。</summary>
    public string ServerConnectionString => _serverConnectionString;

    /// <inheritdoc />
    public async Task<string> ProvisionAsync(CancellationToken ct = default)
    {
        if (!string.IsNullOrEmpty(_connectionString))
        {
            return _connectionString;
        }

        _schemaName = BuildSchemaName("main");
        _connectionString = await CreateSchemaAsync(_schemaName, ct).ConfigureAwait(false);

        // 顺手回收历史残留库（见 SweepStaleSchemasAsync 的说明）。
        // 放在自己建库之后，是为了让「跳过自己」的判断有确定的库名可比。
        await SweepStaleSchemasAsync(ct).ConfigureAwait(false);

        return _connectionString;
    }

    /// <summary>
    /// 回收陈旧的一次性测试库。
    ///
    /// 【为什么需要】
    ///   正常结束时 <see cref="DisposeAsync"/> 会 DROP 自己的库；但只要进程被强杀
    ///   （CI 超时、IDE 停止调试、Ctrl+C、测试宿主崩溃），清理就不会执行，
    ///   测试库便会在共享 MySQL 上越积越多——这在多人共用的开发库上尤其难受。
    ///
    /// 【安全边界】三重护栏，绝无可能误删业务库：
    ///   1. 只看 <see cref="SchemaPrefix"/> 前缀的库；
    ///   2. 必须能从库名解析出合法时间戳，解析不出的一律跳过（宁可漏删不可错删）；
    ///   3. 只删早于阈值（默认 2 小时）的库，避免误伤正在并行运行的另一次测试。
    ///
    /// 阈值由 <c>IOT_TEST_SWEEP_HOURS</c> 控制，置 <c>0</c> 可整体关闭。
    /// 回收失败只打日志，不影响本次测试。
    /// </summary>
    private async Task SweepStaleSchemasAsync(CancellationToken ct)
    {
        var thresholdHours = ResolveSweepHours();
        if (thresholdHours <= 0)
        {
            return;
        }

        try
        {
            var cutoff = DateTime.Now.AddHours(-thresholdHours);
            var stale = new List<string>();

            await using (var connection = new MySqlConnection(_serverConnectionString))
            {
                await connection.OpenAsync(ct).ConfigureAwait(false);
                await using var cmd = connection.CreateCommand();
                cmd.CommandText =
                    "SELECT SCHEMA_NAME FROM information_schema.SCHEMATA " +
                    "WHERE SCHEMA_NAME LIKE @prefix";
                cmd.Parameters.AddWithValue("@prefix", SchemaPrefix + "%");

                await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
                while (await reader.ReadAsync(ct).ConfigureAwait(false))
                {
                    var name = reader.GetString(0);

                    // 跳过自己
                    if (string.Equals(name, _schemaName, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    // 解析不出时间戳的一律不动
                    if (TryParseSchemaTimestamp(name, out var created) && created < cutoff)
                    {
                        stale.Add(name);
                    }
                }
            }

            foreach (var name in stale)
            {
                await DropSchemaCoreAsync(name, ct).ConfigureAwait(false);
            }

            if (stale.Count > 0)
            {
                Console.WriteLine(
                    $"[MySqlDbProvisioner] 已回收 {stale.Count} 个早于 {thresholdHours}h 的残留测试库" +
                    $"（多为进程被强杀所致；设 {SharedTestConstants.EnvVars.SweepHours}=0 可关闭回收）");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MySqlDbProvisioner] !! 回收残留测试库失败（不影响本次测试）：{ex.Message}");
        }
    }

    private static double ResolveSweepHours()
    {
        var raw = Environment.GetEnvironmentVariable(SharedTestConstants.EnvVars.SweepHours);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return DefaultSweepHours;
        }

        return double.TryParse(raw, out var parsed) && parsed >= 0 ? parsed : DefaultSweepHours;
    }

    /// <summary>
    /// 从库名 <c>iot_platform_test_{purpose}_{yyyyMMddHHmmss}_{rand4}</c> 中解析创建时间。
    /// 命名规则一旦变更，这里解析失败 ⇒ 该库不会被回收（安全侧失败）。
    /// </summary>
    private static bool TryParseSchemaTimestamp(string schemaName, out DateTime created)
    {
        created = default;

        var parts = schemaName.Split('_');
        if (parts.Length < 2)
        {
            return false;
        }

        // 时间戳固定位于随机后缀之前
        var stamp = parts[^2];
        return DateTime.TryParseExact(
            stamp,
            "yyyyMMddHHmmss",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out created);
    }

    /// <inheritdoc />
    public async Task<string> ProvisionScratchAsync(string purpose, CancellationToken ct = default)
    {
        var name = BuildSchemaName(purpose);
        return await CreateSchemaAsync(name, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DropScratchAsync(string scratchConnectionString, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(scratchConnectionString))
        {
            return;
        }

        var name = new MySqlConnectionStringBuilder(scratchConnectionString).Database;
        await DropSchemaCoreAsync(name, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DisposeSchemaAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_schemaName))
        {
            return;
        }

        if (string.Equals(Environment.GetEnvironmentVariable(SharedTestConstants.EnvVars.KeepSchema), "1",
                StringComparison.Ordinal))
        {
            Console.WriteLine($"[MySqlDbProvisioner] {SharedTestConstants.EnvVars.KeepSchema}=1，保留测试库 `{_schemaName}` 以便排障");
            return;
        }

        await DropSchemaCoreAsync(_schemaName, ct).ConfigureAwait(false);
        _schemaName = string.Empty;
        _connectionString = string.Empty;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await DisposeSchemaAsync().ConfigureAwait(false);

        // 连接池里可能仍持有指向已删除库的连接，显式清空避免后续误用
        await MySqlConnection.ClearPoolAsync(new MySqlConnection(_serverConnectionString)).ConfigureAwait(false);
    }

    private static string BuildSchemaName(string purpose)
    {
        var safePurpose = SafeIdentifier.IsMatch(purpose) ? purpose : "x";
        var stamp = DateTime.Now.ToString("yyyyMMddHHmmss");
        var rand = Random.Shared.Next(0, 0x10000).ToString("x4");
        return $"{SchemaPrefix}{safePurpose}_{stamp}_{rand}";
    }

    private async Task<string> CreateSchemaAsync(string schemaName, CancellationToken ct)
    {
        await using var connection = new MySqlConnection(_serverConnectionString);
        await connection.OpenAsync(ct).ConfigureAwait(false);

        _serverVersionString ??= connection.ServerVersion;

        await using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText =
                $"CREATE SCHEMA `{schemaName}` CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci;";
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        var builder = new MySqlConnectionStringBuilder(_serverConnectionString)
        {
            Database = schemaName,
            CharacterSet = "utf8mb4"
        };

        Console.WriteLine(
            $"[MySqlDbProvisioner] 已创建测试库 `{schemaName}` @ {builder.Server}:{builder.Port} " +
            $"(server={_serverVersionString}, 来源={TestConnectionStringResolver.ResolvedFrom})");

        return builder.ConnectionString;
    }

    private async Task DropSchemaCoreAsync(string schemaName, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(schemaName))
        {
            return;
        }

        // 安全护栏：只允许删自己造的库
        if (!schemaName.StartsWith(SchemaPrefix, StringComparison.Ordinal) || !SafeIdentifier.IsMatch(schemaName))
        {
            throw new InvalidOperationException(
                $"拒绝 DROP 非测试库 `{schemaName}`（必须以 {SchemaPrefix} 开头且仅含字母数字下划线）");
        }

        try
        {
            await using var connection = new MySqlConnection(_serverConnectionString);
            await connection.OpenAsync(ct).ConfigureAwait(false);
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = $"DROP SCHEMA IF EXISTS `{schemaName}`;";
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            Console.WriteLine($"[MySqlDbProvisioner] 已删除测试库 `{schemaName}`");
        }
        catch (Exception ex)
        {
            // 清理失败不应让整个测试运行红掉，但必须留下明确痕迹，避免测试库堆积无人察觉
            Console.WriteLine($"[MySqlDbProvisioner] !! 删除测试库 `{schemaName}` 失败：{ex.Message}");
        }
    }
}
