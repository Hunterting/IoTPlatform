using System.Text;
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

// 安圣设备发现服务（全局单例 BackgroundService）
builder.Services.AddSingleton<IoTPlatform.Services.IAnShengDiscoveryService, IoTPlatform.Services.AnShengDiscoveryService>();
builder.Services.AddHostedService(sp => (IoTPlatform.Services.AnShengDiscoveryService)sp.GetRequiredService<IoTPlatform.Services.IAnShengDiscoveryService>());

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

builder.Services.AddControllers();

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

app.Run();

/// <summary>
/// 顶级语句生成的 Program 类默认为 internal，集成测试工程需要 public 才能使用
/// WebApplicationFactory&lt;Program&gt;。此处按微软官方文档做法追加一个 public partial 声明，
/// 不含任何成员，对运行时行为零影响。
/// </summary>
public partial class Program { }
