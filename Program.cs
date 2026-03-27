using System.Text;
using IoTPlatform.Data;
using IoTPlatform.Infrastructure.Cache;
using IoTPlatform.Infrastructure.JWT;
using IoTPlatform.Infrastructure.Middleware;
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

// 注册服务
builder.Services.AddScoped<IoTPlatform.Services.IAuthService, IoTPlatform.Services.AuthService>();
builder.Services.AddScoped<IoTPlatform.Services.IUserService, IoTPlatform.Services.UserService>();
builder.Services.AddScoped<IoTPlatform.Services.IRoleService, IoTPlatform.Services.RoleService>();
builder.Services.AddScoped<IoTPlatform.Services.IAreaService, IoTPlatform.Services.AreaService>();
builder.Services.AddScoped<IoTPlatform.Services.IDeviceService, IoTPlatform.Services.DeviceService>();
builder.Services.AddScoped<IoTPlatform.Services.IMonitoringService, IoTPlatform.Services.MonitoringService>();
builder.Services.AddScoped<IoTPlatform.Services.IAlertService, IoTPlatform.Services.AlertService>();
builder.Services.AddScoped<IoTPlatform.Services.IWorkOrderService, IoTPlatform.Services.WorkOrderService>();
builder.Services.AddScoped<IoTPlatform.Services.IAnalyticsService, IoTPlatform.Services.AnalyticsService>();

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

var app = builder.Build();

// 配置HTTP请求管道

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "IoT Platform API v1");
        c.RoutePrefix = "swagger";
    });
}

// 使用异常处理中间件
app.UseMiddleware<ExceptionHandlingMiddleware>();

// 使用操作日志中间件
app.UseMiddleware<OperationLoggingMiddleware>();

app.UseHttpsRedirection();

app.UseCors("AllowAll");

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
