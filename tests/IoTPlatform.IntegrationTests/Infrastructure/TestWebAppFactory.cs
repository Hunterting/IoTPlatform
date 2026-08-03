using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using IoTPlatform.Data;
using IoTPlatform.Infrastructure.Protocol;
using IoTPlatform.IntegrationTests.Infrastructure.Auth;
using IoTPlatform.IntegrationTests.Infrastructure.Mqtt;
using IoTPlatform.Services;
using Pomelo.EntityFrameworkCore.MySql.Infrastructure;
using Xunit;

namespace IoTPlatform.IntegrationTests.Infrastructure;

/// <summary>
/// 基于 WebApplicationFactory&lt;Program&gt; 的测试主机工厂。
///
/// 关键替换（架构方案 §3）：
///   1. UseEnvironment("Testing") —— 跳过 Program.cs 的开发种子，但保留 Database.Migrate()；
///   2. 覆盖 ConnectionStrings:DefaultConnection 指向一次性测试 schema，并用显式 ServerVersion；
///   3. 摘除 3 个 IHostedService（Mqtt / DataRetention / AnShengDiscovery）与 IMqttClientService；
///   4. 用 FakeProtocolAdapterFactory 替换 IProtocolAdapterFactory；
///   5. 把默认认证方案切到 TestAuthHandler（"Test" scheme）。
/// </summary>
public sealed class TestWebAppFactory : WebApplicationFactory<Program>
{
    private readonly string _connectionString;
    private readonly string? _serverVersion;
    private readonly FakeProtocolAdapterFactory _adapterFactory;

    public TestWebAppFactory(string connectionString, string? serverVersion = null)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
        _serverVersion = serverVersion;
        _adapterFactory = new FakeProtocolAdapterFactory();
    }

    /// <summary>录制替身适配器实例，与 TestServer 内生产代码解析到的是同一个对象。</summary>
    public RecordingAnShengAdapter Adapter => _adapterFactory.DefaultAdapter;

    /// <summary>
    /// 替身适配器工厂本体。
    ///
    /// 用例级复位必须走它而非 <see cref="Adapter"/>：后者只是缺省分身，
    /// 通过 <c>GetOrCreateFor(configId)</c> 登记的额外分身只有工厂才清得掉。
    /// </summary>
    public FakeProtocolAdapterFactory AdapterFactory => _adapterFactory;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            // 优先加载测试工程自带的 appsettings.Testing.json（已复制到输出目录）
            var testingSettings = Path.Combine(AppContext.BaseDirectory, "appsettings.Testing.json");
            if (File.Exists(testingSettings))
            {
                config.AddJsonFile(testingSettings, optional: true, reloadOnChange: false);
            }

            // 内存配置覆盖：关键安全项一律指向不可达回环 + 关闭后台服务相关开关，
            // 并强制把业务库连接串替换为一次性测试 schema。
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = _connectionString,
                ["Redis:Enabled"] = "false",
                ["DataRetention:Enabled"] = "false",
                ["AnShengMqtt:Host"] = "127.0.0.1",
                ["AnShengMqtt:Port"] = "1",
                ["MQTT:Server"] = "127.0.0.1",
                ["MQTT:Port"] = "1"
            });
        });

        builder.ConfigureTestServices(services =>
        {
            ReplaceDbContext(services);
            RemoveBackgroundServices(services);
            ReplaceAdapterFactory(services);
            ReplaceAuthentication(services);
        });
    }

    private void ReplaceDbContext(IServiceCollection services)
    {
        // 移除 Program.cs 注册的 DbContextOptions<AppDbContext> 与 AppDbContext，重注册到测试 schema
        var toRemove = services.Where(d =>
            d.ServiceType == typeof(DbContextOptions<AppDbContext>) ||
            d.ServiceType == typeof(AppDbContext) ||
            (d.ServiceType.IsGenericType &&
             d.ServiceType.GetGenericTypeDefinition() == typeof(DbContextOptions<>) &&
             d.ServiceType.GenericTypeArguments[0] == typeof(AppDbContext))).ToList();
        foreach (var d in toRemove)
        {
            services.Remove(d);
        }

        var serverVersion = ParseServerVersion(_serverVersion);
        services.AddDbContext<AppDbContext>((_, options) =>
        {
            options.UseMySql(_connectionString, serverVersion, mysql =>
            {
                mysql.EnableRetryOnFailure(
                    maxRetryCount: 3,
                    maxRetryDelay: TimeSpan.FromSeconds(15),
                    errorNumbersToAdd: null);
            });
        });
    }

    private static void RemoveBackgroundServices(IServiceCollection services)
    {
        // 只摘除后台 IHostedService（Mqtt / DataRetention / AnShengDiscovery 的扫描循环），
        // 但<b>保留</b> IAnShengDiscoveryService 单例注册——
        // 认领链路（AnShengController.ClaimDevice → IAnShengDiscoveryService.ClaimAsync）
        // 在 HTTP 请求作用域内直接消费该单例，若一并摘除，认领端点会 DI 解析失败（500）。
        // AnShengDiscoveryService 的扫描循环由 line 150 的 IHostedService 包装注册承载，
        // 摘掉 IHostedService 即关掉循环，单例本身仍可被控制器与 IAnShengProbeService 复用。
        var toRemove = services.Where(d =>
            d.ServiceType == typeof(IHostedService) ||
            d.ServiceType == typeof(IMqttClientService)).ToList();
        foreach (var d in toRemove)
        {
            services.Remove(d);
        }
    }

    private void ReplaceAdapterFactory(IServiceCollection services)
    {
        var existing = services.FirstOrDefault(d => d.ServiceType == typeof(IProtocolAdapterFactory));
        if (existing != null)
        {
            services.Remove(existing);
        }

        services.AddSingleton<IProtocolAdapterFactory>(_adapterFactory);
    }

    private static void ReplaceAuthentication(IServiceCollection services)
    {
        // AddAuthentication 内部用 TryAdd 注册 AuthenticationOptions，重复调用幂等；
        // 此处再次调用会追加一个 IConfigureOptions<AuthenticationOptions>，把默认方案设为 Test。
        // 标准授权中间件与 PermissionAuthorizeAttribute 都会使用默认方案。
        services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
            options.DefaultChallengeScheme = TestAuthHandler.SchemeName;
            options.DefaultScheme = TestAuthHandler.SchemeName;
            options.DefaultForbidScheme = TestAuthHandler.SchemeName;
        })
        .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
    }

    private static ServerVersion ParseServerVersion(string? raw)
    {
        if (!string.IsNullOrWhiteSpace(raw) && ServerVersion.TryParse(raw, out var parsed))
        {
            return parsed;
        }

        return ServerVersion.Create(8, 0, 36, ServerType.MySql);
    }
}
