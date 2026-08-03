using System.Threading;
using System.Threading.Tasks;
using IoTPlatform.IntegrationTests.Infrastructure.Mqtt;
using MySqlConnector;
using Respawn;
using Respawn.Graph;
using Xunit;

namespace IoTPlatform.IntegrationTests.Infrastructure;

/// <summary>
/// 集合级夹具：整个测试运行只创建/销毁一次一次性 MySQL schema，并持有共享的 TestServer。
///
/// 设计要点（架构方案 §2 / §1.6）：
///   · 单一 schema + 单一 TestServer = 单一 AppCode，规避 EF 模型缓存冻结租户过滤器的坑；
///   · 共享 TestServer 还顺带让 Database.Migrate() 只跑一次（迁移本身也是 T5 的验收对象）；
///   · 用例级清理交给 Respawn（保表结构、全库级清数据） + StaticStateResetter（清静态字典）。
/// </summary>
public sealed class DatabaseFixture : IAsyncLifetime
{
    private readonly IDbProvisioner _provisioner;
    private Respawner? _respawner;
    private TestWebAppFactory? _factory;

    public DatabaseFixture()
    {
        _provisioner = DbProvisionerFactory.Create();
    }

    /// <summary>一次性测试 schema 的完整连接串。</summary>
    public string ConnectionString => _provisioner.ConnectionString;

    /// <summary>一次性测试 schema 名。</summary>
    public string SchemaName => _provisioner.SchemaName;

    /// <summary>共享 TestServer 工厂。</summary>
    public TestWebAppFactory Factory =>
        _factory ?? throw new InvalidOperationException("Fixture 尚未初始化（Factory 不可用）");

    /// <summary>录制替身适配器（= 下发链路的断言锚点）。</summary>
    public RecordingAnShengAdapter Adapter => Factory.Adapter;

    /// <summary>替身适配器工厂，用例级复位入口（含通过 <c>GetOrCreateFor</c> 登记的分身）。</summary>
    public FakeProtocolAdapterFactory AdapterFactory => Factory.AdapterFactory;

    public async Task InitializeAsync()
    {
        // ① 建一次性 schema
        await _provisioner.ProvisionAsync();

        // ② 建共享 TestServer；首次 CreateClient 会触发 Program.cs 的 Database.Migrate() 在真实 MySQL 上建表
        _factory = new TestWebAppFactory(ConnectionString, _provisioner.ServerVersionString);

        // 触发启动 + 迁移（探针请求足以拉起完整管道；结果不重要）
        using var probe = _factory.CreateClient();
        _ = await probe.GetAsync("/swagger/v1/swagger.json");

        // ③ 迁移完成后建立 Respawner（此时所有表已存在）
        //    · 【必须走 DbConnection 重载】Respawn 的 connectionString 重载内部写死 SqlDataAdapter，
        //      对 MySQL 会抛 "This overload only supports the SqlDataAdapter"；且连接必须先 Open。
        //    · 必须忽略 __EFMigrationsHistory，否则清库会把迁移记录一并删掉，
        //      导致同进程内任何再次 Migrate() 重放全部迁移并撞「表已存在」。
        await using (var conn = new MySqlConnection(ConnectionString))
        {
            await conn.OpenAsync();
            _respawner = await Respawner.CreateAsync(conn, new RespawnerOptions
            {
                DbAdapter = DbAdapter.MySql,
                SchemasToInclude = new[] { SchemaName },
                TablesToIgnore = new Table[] { "__EFMigrationsHistory" }
            });
        }

        // ④ 首次置空，确保基线干净
        await ResetAsync();
    }

    /// <summary>用例级清理：清空全部业务表（保留表结构），供下一用例从干净基线播种。</summary>
    public async Task ResetAsync()
    {
        if (_respawner == null)
        {
            return;
        }

        // 同上：ResetAsync 也必须传已 Open 的 MySqlConnection，不能传连接串。
        await using var conn = new MySqlConnection(ConnectionString);
        await conn.OpenAsync();
        await _respawner.ResetAsync(conn);
    }

    public async Task DisposeAsync()
    {
        if (_factory != null)
        {
            _factory.Dispose();
        }

        await _provisioner.DisposeAsync();
    }
}
