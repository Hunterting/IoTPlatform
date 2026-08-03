using Microsoft.Extensions.Configuration;
using MySqlConnector;

namespace IoTPlatform.IntegrationTests.Infrastructure;

/// <summary>
/// 解析「测试 MySQL 服务器」连接串。
///
/// 【纪律】仓库里不出现任何明文账号密码。解析优先级：
///   1. 环境变量 <c>IOT_TEST_MYSQL</c>（推荐，CI/本地都用它）；
///   2. 测试输出目录下 <c>appsettings.Testing.json</c> 的 <c>ConnectionStrings:TestMySql</c>（默认留空）；
///   3. 仓库根 <c>appsettings.json</c> / <c>appsettings.Development.json</c> 的
///      <c>ConnectionStrings:DefaultConnection</c>——<b>只取服务器与账号，Database 一律丢弃</b>，
///      测试永远在自己新建的一次性 schema 上跑，绝不碰业务库。
/// 三条都拿不到 ⇒ 抛出带操作指引的异常。
/// </summary>
public static class TestConnectionStringResolver
{
    /// <summary>环境变量名。</summary>
    public const string EnvVarName = SharedTestConstants.EnvVars.MySqlConnection;

    /// <summary>
    /// 解析出「不含 Database」的服务器级连接串。
    /// </summary>
    /// <returns>可直接用于 <c>CREATE SCHEMA</c> 的连接串。</returns>
    /// <exception cref="InvalidOperationException">三级回退全部落空时抛出。</exception>
    public static string ResolveServerConnectionString()
    {
        var raw = ResolveRaw(out var source);
        var builder = new MySqlConnectionStringBuilder(raw)
        {
            // 一次性库由 provisioner 建，这里必须清空 Database，避免误连业务库
            Database = string.Empty
        };

        if (string.IsNullOrWhiteSpace(builder.CharacterSet))
        {
            builder.CharacterSet = "utf8mb4";
        }

        // 建库/删库属于 DDL，给足超时；同时禁用连接池复用带来的库上下文残留
        if (builder.ConnectionTimeout == 0)
        {
            builder.ConnectionTimeout = 15;
        }

        ResolvedFrom = source;
        return builder.ConnectionString;
    }

    /// <summary>
    /// 上一次解析命中的来源描述，仅用于日志与失败排查。
    /// </summary>
    public static string ResolvedFrom { get; private set; } = "(未解析)";

    /// <summary>
    /// 定位仓库根目录（含 <c>IoTPlatform.csproj</c> 的目录），找不到返回 null。
    /// </summary>
    /// <returns>仓库根绝对路径或 null。</returns>
    public static string? FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "IoTPlatform.csproj")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        return null;
    }

    private static string ResolveRaw(out string source)
    {
        // ① 环境变量
        var fromEnv = Environment.GetEnvironmentVariable(EnvVarName);
        if (!string.IsNullOrWhiteSpace(fromEnv))
        {
            source = $"环境变量 {EnvVarName}";
            return fromEnv.Trim();
        }

        // ② 测试工程 appsettings.Testing.json
        var testSettingsPath = Path.Combine(AppContext.BaseDirectory, "appsettings.Testing.json");
        if (File.Exists(testSettingsPath))
        {
            var cfg = new ConfigurationBuilder()
                .AddJsonFile(testSettingsPath, optional: true, reloadOnChange: false)
                .Build();
            var fromTestFile = cfg.GetConnectionString("TestMySql");
            if (!string.IsNullOrWhiteSpace(fromTestFile))
            {
                source = "appsettings.Testing.json:ConnectionStrings:TestMySql";
                return fromTestFile.Trim();
            }
        }

        // ③ 仓库根 appsettings.json / appsettings.Development.json 的 DefaultConnection
        var repoRoot = FindRepositoryRoot();
        if (repoRoot != null)
        {
            var cfg = new ConfigurationBuilder()
                .SetBasePath(repoRoot)
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
                .AddJsonFile("appsettings.Development.json", optional: true, reloadOnChange: false)
                .Build();

            var fromRepo = cfg.GetConnectionString("DefaultConnection");
            if (!string.IsNullOrWhiteSpace(fromRepo))
            {
                source = "仓库根 appsettings*.json:ConnectionStrings:DefaultConnection（已剥离 Database）";
                return fromRepo.Trim();
            }
        }

        throw new InvalidOperationException(
            $"""
             无法确定测试 MySQL 连接串。请设置环境变量 {EnvVarName}，例如：

               PowerShell : $env:{EnvVarName} = "Server=192.168.3.7;Port=3306;User=root;Password=<凭据>;"
               CMD        : set {EnvVarName}=Server=192.168.3.7;Port=3306;User=root;Password=<凭据>;
               bash       : export {EnvVarName}='Server=192.168.3.7;Port=3306;User=root;Password=<凭据>;'

             说明：
               · Database 无需填写（填了也会被忽略），测试会自建一次性 schema 并在结束后 DROP；
               · 该账号需要 CREATE / DROP SCHEMA 权限；
               · 详见 tests/IoTPlatform.IntegrationTests/README.md。
             """);
    }
}
