using System.Text;
using System.Text.Json.Serialization;
using IoTPlatform.Data;
using IoTPlatform.Data.Repositories.Interfaces;
using IoTPlatform.Data.Repositories.Implementations;
using IoTPlatform.Data.SeedData;
using IoTPlatform.DTOs.Profiles;
using IoTPlatform.Hubs;
using IoTPlatform.Helpers;
using IoTPlatform.Infrastructure.Cache;
using IoTPlatform.Infrastructure.JWT;
using IoTPlatform.Infrastructure.Middleware;
using IoTPlatform.Infrastructure.Protocol;
using IoTPlatform.Infrastructure.Tenant;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Serilog;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

// 配置Serilog
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File("logs/log-.txt", rollingInterval: RollingInterval.Day)
    .CreateLogger();

builder.Host.UseSerilog();

// 添加服务到容器

// 注册JWT Helper
builder.Services.AddSingleton<JwtHelper>();

// 注册HttpContextAccessor（租户上下文需要）
// 确保仅在未注册时添加 IHttpContextAccessor，避免重复注册
if (!builder.Services.Any(s => s.ServiceType == typeof(Microsoft.AspNetCore.Http.IHttpContextAccessor)))
{
    builder.Services.AddHttpContextAccessor();
}

// 注册协议适配器工厂
builder.Services.AddSingleton<IProtocolAdapterFactory, ProtocolAdapterFactory>();

// 注册租户上下文服务
builder.Services.AddScoped<ITenantContextAccessor, ScopedTenantContextAccessor>();

// 注册Redis缓存
builder.Services.AddSingleton<IRedisCacheService, RedisCacheService>();

// 注册DbContext
builder.Services.AddDbContext<AppDbContext>(options =>
{
    var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
    options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString),
        mysqlOptions =>
        {
            mysqlOptions.EnableRetryOnFailure(
                maxRetryCount: 5,
                maxRetryDelay: TimeSpan.FromSeconds(30),
                errorNumbersToAdd: null);
        });
});

// 注册AutoMapper
builder.Services.AddAutoMapper(typeof(MappingProfile));

// 注册仓储服务
builder.Services.AddScoped(typeof(IRepository<>), typeof(Repository<>));
builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IRoleRepository, RoleRepository>();
builder.Services.AddScoped<ICustomerRepository, CustomerRepository>();
builder.Services.AddScoped<IProjectRepository, ProjectRepository>();
builder.Services.AddScoped<IAreaRepository, AreaRepository>();
builder.Services.AddScoped<IDeviceRepository, DeviceRepository>();
builder.Services.AddScoped<IAlertRecordRepository, AlertRecordRepository>();
builder.Services.AddScoped<IWorkOrderRepository, WorkOrderRepository>();
builder.Services.AddScoped<IArchiveRepository, ArchiveRepository>();
builder.Services.AddScoped<IDictionaryRepository, DictionaryRepository>();
builder.Services.AddScoped<ISystemSettingRepository, SystemSettingRepository>();
builder.Services.AddScoped<ILogRepository, LogRepository>();
builder.Services.AddScoped<IMonitoringRepository, MonitoringRepository>();
builder.Services.AddScoped<IDataRuleRepository, DataRuleRepository>();
builder.Services.AddScoped<IProtocolConfigRepository, ProtocolConfigRepository>();
builder.Services.AddScoped<IETLTaskRepository, ETLTaskRepository>();

// 注册种子数据服务
builder.Services.AddScoped<DataSeeder>();
builder.Services.AddScoped<SeedRoles>();
builder.Services.AddScoped<SeedUsers>();
builder.Services.AddScoped<SeedCustomers>();
builder.Services.AddScoped<SeedDictionaries>();

// 注册服务
builder.Services.AddScoped<IoTPlatform.Services.IAuthService, IoTPlatform.Services.AuthService>();
builder.Services.AddScoped<IoTPlatform.Services.IUserService, IoTPlatform.Services.UserService>();
builder.Services.AddScoped<IoTPlatform.Services.IRoleService, IoTPlatform.Services.RoleService>();
builder.Services.AddScoped<IoTPlatform.Services.IPermissionService, IoTPlatform.Services.PermissionService>();
builder.Services.AddScoped<IoTPlatform.Services.IAreaService, IoTPlatform.Services.AreaService>();
builder.Services.AddScoped<IoTPlatform.Services.IDeviceService, IoTPlatform.Services.DeviceService>();
builder.Services.AddScoped<IoTPlatform.Services.IMonitoringService, IoTPlatform.Services.MonitoringService>();
builder.Services.AddScoped<IoTPlatform.Services.IAlertService, IoTPlatform.Services.AlertService>();
builder.Services.AddScoped<IoTPlatform.Services.IWorkOrderService, IoTPlatform.Services.WorkOrderService>();
builder.Services.AddScoped<IoTPlatform.Services.IAnalyticsService, IoTPlatform.Services.AnalyticsService>();
// 辅助功能模块服务
builder.Services.AddScoped<IoTPlatform.Services.IArchiveService, IoTPlatform.Services.ArchiveService>();
builder.Services.AddScoped<IoTPlatform.Services.ILogService, IoTPlatform.Services.LogService>();
builder.Services.AddScoped<IoTPlatform.Services.IDictionaryService, IoTPlatform.Services.DictionaryService>();
builder.Services.AddScoped<IoTPlatform.Services.ISettingsService, IoTPlatform.Services.SettingsService>();

// 文件存储服务
builder.Services.AddScoped<IoTPlatform.Services.FileStorageService, IoTPlatform.Services.FileStorageService>();

// 高级功能模块服务
builder.Services.AddScoped<IoTPlatform.Services.IProtocolConfigService, IoTPlatform.Services.ProtocolConfigService>();
builder.Services.AddScoped<IoTPlatform.Services.IDataRuleService, IoTPlatform.Services.DataRuleService>();
builder.Services.AddScoped<IoTPlatform.Services.IETLTaskService, IoTPlatform.Services.ETLTaskService>();
builder.Services.AddScoped<IoTPlatform.Services.IGatewayService, IoTPlatform.Services.GatewayService>();
builder.Services.AddScoped<IoTPlatform.Services.ITunnelService, IoTPlatform.Services.TunnelService>();
builder.Services.AddScoped<IoTPlatform.Services.IPluginService, IoTPlatform.Services.PluginService>();
builder.Services.AddScoped<IoTPlatform.Services.IDatabaseConfigService, IoTPlatform.Services.DatabaseConfigService>();
builder.Services.AddScoped<IoTPlatform.Services.IDataCollectionService, IoTPlatform.Services.DataCollectionService>();
builder.Services.AddScoped<IoTPlatform.Services.Interfaces.ITimeSeriesStore, IoTPlatform.Services.MySqlTimeSeriesStore>();
builder.Services.AddSingleton<IoTPlatform.Services.IMqttClientService, IoTPlatform.Services.MqttClientService>();
builder.Services.AddHostedService<IoTPlatform.Services.MqttHostedService>();
builder.Services.AddHostedService<IoTPlatform.Services.DataRetentionHostedService>();

// 设备指令服务
builder.Services.AddScoped<IoTPlatform.Services.IDeviceCommandService, IoTPlatform.Services.DeviceCommandService>();

// 安圣 MQTT 命令服务
builder.Services.AddScoped<IoTPlatform.Services.IAnShengCommandService, IoTPlatform.Services.AnShengCommandService>();

// ───────────────────────────────────────────────────────────────────────────
// T7 命令服务重构：配置 / 校验闸门 / 超时清扫宿主
//
// ★ 这三行缺一，AnShengController 的<b>全部</b>端点都会在首个请求时抛
//   InvalidOperationException（Unable to resolve service for type ...）。
//   注意它<b>不会</b>在启动时暴露：本项目未开启 ValidateOnBuild / ValidateScopes，
//   .NET 的 DI 图是惰性校验的 —— 「dotnet run 起得来」不等于「接线接对了」。
//   这正是 QA Round 1 的 P0-1：编译零错误、单测全绿，运行期整片 500。
//
// 生命周期理由：
//   · AnShengCommandOptions —— Options 模式，与 Probe/Event 两处保持同一范式；
//   · AnShengCommandGuard   —— Scoped。本身无状态（纯函数、零字段），
//       选 Scoped 而非 Singleton 是为了与唯一消费方 AnShengCommandService 同域，
//       将来若需注入租户/审计上下文不必改注册（见 Guard 类注释）；
//   · SweepHostedService    —— Hosted（进程级单例）。它经 IServiceScopeFactory
//       自建 scope 取 AppDbContext，构造函数里绝不可直接注入任何 Scoped 服务。
// ───────────────────────────────────────────────────────────────────────────
builder.Services.Configure<IoTPlatform.Configuration.AnShengCommandOptions>(
    builder.Configuration.GetSection(IoTPlatform.Configuration.AnShengCommandOptions.SectionName));
builder.Services.AddScoped<IoTPlatform.Services.AnShengCommandGuard>();
builder.Services.AddHostedService<IoTPlatform.Services.AnShengCommandSweepHostedService>();

// 安圣设备能力档案服务（Scoped：内部持有 AppDbContext，不可被 Singleton 构造注入）
builder.Services.AddScoped<IoTPlatform.Services.IAnShengDeviceProfileService, IoTPlatform.Services.AnShengDeviceProfileService>();

// ───────────────────────────────────────────────────────────────────────────
// T8 安圣延时任务调度服务（开关动作 / 延时任务镜像）
//
// ★ 这一行缺失，AnShengSwitchController 的全部端点、以及 AnShengMessageRouter /
//   DelayEventHandler 的构造都会在<b>首个上行报文或首个请求</b>时抛
//   InvalidOperationException —— 与 T7 那次 P0 完全同型（本项目未开
//   ValidateOnBuild，DI 图惰性校验，「起得来」不代表「接得上」）。
//
// 生命周期 Scoped 的三条理由：
//   1. 内部持有 AppDbContext，Singleton 化会造成跨请求共享 DbContext；
//   2. 消费方 AnShengMessageRouter / DelayEventHandler 本身就是 Scoped，同域最省事；
//   3. 写后回读需要脱离当前作用域，已由内部注入的 IServiceScopeFactory（Singleton）解决，
//      不需要把整个服务提升为 Singleton。
// ───────────────────────────────────────────────────────────────────────────
builder.Services.AddScoped<IoTPlatform.Services.Interfaces.IAnShengScheduleService,
    IoTPlatform.Services.AnShengScheduleService>();

// ───────────────────────────────────────────────────────────────────────────
// T11 安圣电量计服务（实时 / 统计 / 校准）
//
// ★ 这一行缺失，AnShengEnergyController 的全部端点、以及 AnShengMessageRouter 的构造
//   都会在<b>首个上行报文或首个请求</b>时抛 InvalidOperationException —— 与 T8/T10 那次 P0
//   完全同型（本项目未开 ValidateOnBuild，DI 图惰性校验，「起得来」不代表「接得上」）。
//
// 生命周期 Scoped 的三条理由（同 T8）：
//   1. 内部持有 AppDbContext，Singleton 化会造成跨请求共享 DbContext；
//   2. 消费方 AnShengMessageRouter 本身就是 Scoped，同域最省事；
//   3. 写后回读需要脱离当前作用域，已由内部注入的 IServiceScopeFactory（Singleton）解决，
//      不需要把整个服务提升为 Singleton。
// ───────────────────────────────────────────────────────────────────────────
builder.Services.AddScoped<IoTPlatform.Services.Interfaces.IAnShengEnergyService,
    IoTPlatform.Services.AnShengEnergyService>();

// 安圣同步探测服务
// Singleton 是硬要求：它在构造函数里订阅静态上行总线 AnShengUplinkHub。
// 若注册为 Scoped，每个请求都会新订阅一次却从不退订，事件链会无限增长直至内存耗尽。
builder.Services.Configure<IoTPlatform.Configuration.AnShengProbeOptions>(
    builder.Configuration.GetSection(IoTPlatform.Configuration.AnShengProbeOptions.SectionName));
builder.Services.AddSingleton<IoTPlatform.Services.IAnShengProbeService, IoTPlatform.Services.AnShengProbeService>();

// 安圣设备发现服务（全局单例 BackgroundService）
builder.Services.AddSingleton<IoTPlatform.Services.IAnShengDiscoveryService, IoTPlatform.Services.AnShengDiscoveryService>();
builder.Services.AddHostedService(sp => (IoTPlatform.Services.AnShengDiscoveryService)sp.GetRequiredService<IoTPlatform.Services.IAnShengDiscoveryService>());

// ───────────────────────────────────────────────────────────────────────────
// T6 安圣事件识别与处理管道
//
// 生命周期约束（设计文档 §7.3）：
//   · Pipeline / PendingCommandStore / OfflineDebouncer = Singleton（与 AnShengProbeService 一致）
//   · Router / Dispatcher / Handler / Normalizer        = Scoped（要用 AppDbContext）
//   · Singleton 绝不可直接注入 Scoped —— Pipeline 与 Debouncer 一律经
//     IServiceScopeFactory.CreateScope() 取用 Scoped 服务。
// ───────────────────────────────────────────────────────────────────────────
builder.Services.Configure<IoTPlatform.Configuration.AnShengEventOptions>(
    builder.Configuration.GetSection(IoTPlatform.Configuration.AnShengEventOptions.SectionName));

// 报文解析器无状态，Singleton 即可（既有代码多处 new，这里为 DI 消费方提供一份共享实例）
builder.Services.AddSingleton<IoTPlatform.Infrastructure.Protocol.AnSheng.AnShengMessageParser>();

builder.Services.AddSingleton<IoTPlatform.Services.Interfaces.IAnShengPendingCommandStore,
    IoTPlatform.Services.AnShengPendingCommandStore>();
builder.Services.AddSingleton<IoTPlatform.Services.AnShengOfflineDebouncer>();

// ★ Pipeline 在构造函数里订阅静态上行总线 AnShengUplinkHub。
//   必须是 Singleton：注册为 Scoped 会每个请求订阅一次却从不退订，事件链无限增长。
//   且必须在 app.Run() 之前被解析一次（见文件末尾），否则惰性构造永不发生、订阅永不生效。
builder.Services.AddSingleton<IoTPlatform.Infrastructure.Protocol.AnSheng.AnShengUplinkPipeline>();

builder.Services.AddScoped<IoTPlatform.Infrastructure.Protocol.AnSheng.AnShengDataNormalizer>();
builder.Services.AddScoped<IoTPlatform.Infrastructure.Protocol.AnSheng.AnShengMessageRouter>();
builder.Services.AddScoped<IoTPlatform.Services.AnShengEventDispatcher>();

// 事件责任链：7 个 Handler 全部注册到 IAnShengEventHandler 集合，
// 由 AnShengEventDispatcher 在构造时建 O(1) 索引并校验覆盖完整性（缺一即抛）。
builder.Services.AddScoped<IoTPlatform.Services.Interfaces.IAnShengEventHandler,
    IoTPlatform.Services.AnShengEventHandlers.ConnectedEventHandler>();
builder.Services.AddScoped<IoTPlatform.Services.Interfaces.IAnShengEventHandler,
    IoTPlatform.Services.AnShengEventHandlers.CloseEventHandler>();
builder.Services.AddScoped<IoTPlatform.Services.Interfaces.IAnShengEventHandler,
    IoTPlatform.Services.AnShengEventHandlers.KeyEventHandler>();
builder.Services.AddScoped<IoTPlatform.Services.Interfaces.IAnShengEventHandler,
    IoTPlatform.Services.AnShengEventHandlers.DelayEventHandler>();
builder.Services.AddScoped<IoTPlatform.Services.Interfaces.IAnShengEventHandler,
    IoTPlatform.Services.AnShengEventHandlers.TimeEventHandler>();
builder.Services.AddScoped<IoTPlatform.Services.Interfaces.IAnShengEventHandler,
    IoTPlatform.Services.AnShengEventHandlers.Recv485EventHandler>();
builder.Services.AddScoped<IoTPlatform.Services.Interfaces.IAnShengEventHandler,
    IoTPlatform.Services.AnShengEventHandlers.SimCheckEventHandler>();

// 受控设备服务
builder.Services.AddScoped<IoTPlatform.Services.Interfaces.IControlledDeviceService, IoTPlatform.Services.ControlledDeviceService>();

// 配置SignalR
builder.Services.AddSignalR();

// 配置CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

// 配置JWT认证
var jwtSettings = builder.Configuration.GetSection("Jwt");
var secretKey = jwtSettings["SecretKey"];

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidIssuer = jwtSettings["Issuer"],
        ValidateAudience = true,
        ValidAudience = jwtSettings["Audience"],
        ValidateLifetime = true,
        ClockSkew = TimeSpan.Zero,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey!))
    };
});

builder.Services.AddAuthorization();

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });

// 配置Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "IoT Platform API",
        Version = "v1",
        Description = "物联网平台后端API文档"
    });

    // 将 IFormFile 映射为 binary string，防止 Swashbuckle schema 反射失败
    c.MapType<IFormFile>(() => new OpenApiSchema { Type = "string", Format = "binary" });
    c.MapType<List<IFormFile>>(() => new OpenApiSchema
    {
        Type = "array",
        Items = new OpenApiSchema { Type = "string", Format = "binary" }
    });

    // 添加文件上传过滤器（使用 Filters 命名空间下的新版本）
    c.OperationFilter<IoTPlatform.Filters.FileUploadOperationFilter>();

    // 添加XML注释支持
    var xmlFile = $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    if (File.Exists(xmlPath))
    {
        c.IncludeXmlComments(xmlPath);
    }

    // 解决泛型类型冲突 - 为每个泛型实例生成唯一schemaId
    c.CustomSchemaIds(modelType =>
    {
        // 1. 如果是泛型类型，使用完整泛型签名
        if (modelType.IsGenericType)
        {
            var genericName = modelType.Name.Split('`')[0];
            var typeArgs = modelType.GetGenericArguments();
            var args = string.Join("_", typeArgs.Select(t => GetUniqueSchemaId(t)));
            return $"{genericName}_{args}";
        }
        
        // 2. 对于 Controllers 中的类型，添加前缀
        if (modelType.FullName?.StartsWith("IoTPlatform.Controllers.") == true)
        {
            return "Controller." + modelType.Name;
        }
        
        // 3. 默认使用完整命名空间 + 类型名
        return modelType.FullName ?? modelType.Name;
    });

    // 配置JWT认证
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using the Bearer scheme",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer"
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

string GetUniqueSchemaId(Type type)
{
    if (type.IsGenericType)
    {
        var genericName = type.Name.Split('`')[0];
        var typeArgs = type.GetGenericArguments();
        var args = string.Join("_", typeArgs.Select(t => GetUniqueSchemaId(t)));
        return $"{genericName}_{args}";
    }
    // 移除命名空间前缀，保留简短名称
    return type.Name;
}

var app = builder.Build();

// 自动应用数据库迁移
using (var scope = app.Services.CreateScope())
{
    try
    {
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
        
        logger.LogInformation("检查并应用数据库迁移...");
        context.Database.Migrate();
        logger.LogInformation("数据库迁移完成");
    }
    catch (Exception ex)
    {
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "应用数据库迁移时发生错误");
        // 在开发环境可以选择继续运行，生产环境应该终止
        if (app.Environment.IsDevelopment())
        {
            logger.LogWarning("开发环境：尽管迁移失败，应用将继续启动");
        }
        else
        {
            throw;
        }
    }
}

// 初始化种子数据（如果是首次运行）
if (app.Environment.IsDevelopment())
{
    using (var scope = app.Services.CreateScope())
    {
        try
        {
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            // 检查数据库连接是否可用
            if (context.Database.CanConnect())
            {
                // 使用Task.Run来避免顶级语句中的await问题
                await Task.Run(async () => await context.SeedDataForDevelopmentAsync(scope.ServiceProvider));
            }
            else
            {
                var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
                logger.LogWarning("数据库连接不可用，跳过种子数据初始化");
            }
        }
        catch (Exception ex)
        {
            var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
            logger.LogError(ex, "初始化开发环境种子数据时发生错误");
        }
    }
}

// 配置HTTP请求管道

// 在所有环境启用Swagger（方便调试）
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "IoT Platform API v1");
    c.RoutePrefix = "swagger";
    // 启用深度链接
    c.EnableDeepLinking();
});

// 使用异常处理中间件
app.UseMiddleware<ExceptionHandlingMiddleware>();

// 使用操作日志中间件
app.UseMiddleware<OperationLoggingMiddleware>();

app.UseHttpsRedirection();

app.UseCors("AllowAll");

// 租户上下文初始化（在认证之前初始化，确保后续服务可以使用租户信息）
app.UseTenantInitialization();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// 配置SignalR
app.MapHub<DeviceHub>("/hubs/device");

// ★★★ T6 关键：强制解析 AnShengUplinkPipeline ★★★
//
// DI 的 Singleton 是惰性构造的。Pipeline 在<b>构造函数里</b>订阅静态总线 AnShengUplinkHub，
// 若没有任何地方解析它，构造函数永远不执行 ⇒ 订阅永远不发生 ⇒
// 所有安圣事件（含 close 遗嘱与 30 秒离线去抖）会<b>静默</b>失效：不报错、就是没数据。
// 这是本次改动最容易被误删的一行，删除前请先读 AnShengUplinkPipeline 的类注释。
app.Services.GetRequiredService<IoTPlatform.Infrastructure.Protocol.AnSheng.AnShengUplinkPipeline>();

app.Run();

/// <summary>
/// 顶级语句生成的 Program 类默认为 internal，集成测试工程需要 public 才能使用
/// WebApplicationFactory&lt;Program&gt;。此处按微软官方文档做法追加一个 public partial 声明，
/// 不含任何成员，对运行时行为零影响。
/// </summary>
public partial class Program { }
