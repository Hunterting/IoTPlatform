using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using IoTPlatform.Data;
using IoTPlatform.IntegrationTests.Infrastructure.Mqtt;
using IoTPlatform.IntegrationTests.Seed;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace IoTPlatform.IntegrationTests.Infrastructure;

/// <summary>
/// 所有集成用例的基类（架构方案 §3.8）。
///
/// 【每个用例的生命周期】
///   InitializeAsync：Respawn 清库 → 清静态字典 → 清录制适配器 → 播种基线 → 造 HttpClient
///   （测试方法体）
///   DisposeAsync   ：释放 HttpClient（TestServer 与 schema 由集合级 <see cref="DatabaseFixture"/> 统管）
///
/// 【顺序为什么重要】
///   必须「先清库、再清静态、最后播种」。反过来播种在前会被 Respawn 清掉；
///   静态字典若在播种后才清，用例 A 注册的设备型号会影响本用例的目录校验分支。
///
/// 【并行】xUnit 集合已禁并行（见 xunit.runner.json 与 IntegrationTestCollection），
///   因为所有用例共享同一 schema、同一 TestServer、同一进程级静态字典。
/// </summary>
[Collection(SharedTestConstants.CollectionName)]
public abstract class IntegrationTestBase : IAsyncLifetime
{
    private HttpClient? _client;
    private SeedResult? _seed;

    protected IntegrationTestBase(DatabaseFixture fixture)
    {
        Fixture = fixture ?? throw new ArgumentNullException(nameof(fixture));
    }

    /// <summary>集合级夹具：一次性 schema + 共享 TestServer。</summary>
    protected DatabaseFixture Fixture { get; }

    /// <summary>默认匿名的测试客户端。用 <c>Client.AsAdmin()</c> 等扩展切换身份。</summary>
    protected HttpClient Client =>
        _client ?? throw new InvalidOperationException("用例尚未初始化（Client 不可用）");

    /// <summary>本用例的基线播种结果，含全部真实自增主键。</summary>
    protected SeedResult Seed =>
        _seed ?? throw new InvalidOperationException("用例尚未初始化（Seed 不可用）");

    /// <summary>录制替身适配器，下发链路的断言锚点。</summary>
    protected RecordingAnShengAdapter Adapter => Fixture.Adapter;

    /// <summary>
    /// JSON 反序列化选项：与服务端 <c>AddControllers().AddJsonOptions(...)</c> 保持对称。
    ///
    /// 【camelCase】与 ASP.NET Core 默认的 camelCase 对齐。
    /// 【枚举按字符串】服务端已全局注册 <see cref="JsonStringEnumConverter"/>，
    ///   枚举以原名（PascalCase，如 "RejectedByKind"）出网；测试客户端必须装同一个
    ///   转换器才能读回强类型 DTO，否则 <c>ReadFromJsonAsync</c> 会抛
    ///   "The JSON value could not be converted to ...Enum"。
    /// </summary>
    protected static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public virtual async Task InitializeAsync()
    {
        // ① 清数据（保表结构）
        await Fixture.ResetAsync();

        // ② 清进程级静态状态——不清会造成「单跑绿、连跑红」
        //    传入根 Provider，才能顺带清掉 Singleton 探测服务的在途等待表。
        //    ⚠ 这里清的是「在途等待」，绝不是「总线订阅」：
        //    AnShengProbeService 是 Singleton 且构造时订阅静态总线，TestServer 全程唯一，
        //    调 AnShengUplinkHub.Reset() 会让后续所有用例的探测永久超时。
        StaticStateResetter.ResetAll(Fixture.Factory.Services);

        // ③ 清录制适配器。走工厂而非 Adapter.Reset()：
        //    Adapter 只是缺省分身，用例通过 GetOrCreateFor(configId) 登记的额外分身
        //    只有工厂才清得掉，否则会跨用例泄漏录制内容。
        Fixture.AdapterFactory.Reset();

        // ④ 播种基线
        await using (var scope = CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            _seed = await SeedData.SeedAsync(db);
        }

        // ⑤ 造客户端。AllowAutoRedirect=false：本平台 API 不应有 302，
        //    关掉自动跳转能让「意外重定向」以断言失败的形式暴露，而不是被静默跟随。
        _client = Fixture.Factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        await OnInitializedAsync();
    }

    public virtual Task DisposeAsync()
    {
        _client?.Dispose();
        _client = null;
        _seed = null;
        return Task.CompletedTask;
    }

    /// <summary>
    /// 子类扩展点：基线播种完成、Client 就绪后触发，用于追加本用例特有的数据。
    /// </summary>
    protected virtual Task OnInitializedAsync() => Task.CompletedTask;

    /// <summary>
    /// 开一个 DI 作用域。<b>用完必须释放</b>，否则 AppDbContext 连接不归池。
    /// </summary>
    protected AsyncServiceScope CreateScope() => Fixture.Factory.Services.CreateAsyncScope();

    /// <summary>
    /// 在独立作用域里直接查库，绕过 HTTP 管道做「落库副作用」断言。
    /// </summary>
    /// <example>
    /// <code>
    /// var count = await QueryDbAsync(db => db.Devices.CountAsync());
    /// </code>
    /// </example>
    protected async Task<T> QueryDbAsync<T>(Func<AppDbContext, Task<T>> query)
    {
        ArgumentNullException.ThrowIfNull(query);

        await using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await query(db);
    }

    /// <summary>
    /// 在独立作用域里执行写操作（如追加测试数据）。
    /// </summary>
    protected async Task ExecuteDbAsync(Func<AppDbContext, Task> action)
    {
        ArgumentNullException.ThrowIfNull(action);

        await using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await action(db);
    }

    /// <summary>
    /// 读取响应体并反序列化为 <c>ApiResponse&lt;T&gt;</c>。
    ///
    /// 【为什么不用 EnsureSuccessStatusCode】
    ///   业务失败时本平台仍返回 HTTP 200，靠包体内 Code 表达状态，
    ///   因此业务断言必须落在 Code 上，而不是 HTTP 状态码。
    ///
    /// 【例外：匿名请求没有包体，不要对它调用本方法】
    ///   未携带任何 <c>X-Test-*</c> 头的请求会被 AuthorizationMiddleware 在进入 MVC 过滤器之前
    ///   直接挑战，返回<b>裸 HTTP 401 且包体为空</b>——此时应断言 <c>StatusCode</c>，
    ///   调用本方法会因空包体反序列化失败。参见 示例-01 / 示例-08。
    ///   （<c>PermissionAuthorizeAttribute</c> 里那段返回 <c>ApiResponse.Unauthorized</c> 的分支
    ///   对匿名请求实际是死代码；已认证但越权才会走到 HTTP 200 + Code 403。）
    /// </summary>
    protected static async Task<T?> ReadAsync<T>(HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);
        return await response.Content.ReadFromJsonAsync<T>(JsonOptions);
    }
}
