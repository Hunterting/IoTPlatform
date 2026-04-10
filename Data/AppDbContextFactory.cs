using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace IoTPlatform.Data;

/// <summary>
/// 设计时 DbContext 工厂
/// 用于 EF Core 工具（如迁移命令）
/// </summary>
public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();

        // 确定基础路径
        var basePath = Directory.GetCurrentDirectory();

        // 尝试从 appsettings.json 读取连接字符串
        var configuration = new ConfigurationBuilder()
            .SetBasePath(basePath)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .AddJsonFile("appsettings.Development.json", optional: true, reloadOnChange: false)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = configuration.GetConnectionString("DefaultConnection");

        // 如果仍未获取到连接字符串，尝试直接读取文件
        if (string.IsNullOrEmpty(connectionString))
        {
            var appsettingsPath = Path.Combine(basePath, "appsettings.json");
            if (File.Exists(appsettingsPath))
            {
                var jsonContent = File.ReadAllText(appsettingsPath);
                var jsonDoc = System.Text.Json.JsonDocument.Parse(jsonContent);
                if (jsonDoc.RootElement.TryGetProperty("ConnectionStrings", out var connSection) &&
                    connSection.TryGetProperty("DefaultConnection", out var connStr))
                {
                    connectionString = connStr.GetString();
                }
            }
        }

        if (string.IsNullOrEmpty(connectionString))
        {
            throw new InvalidOperationException(
                $"未找到数据库连接字符串。请确保 appsettings.json 中配置了 ConnectionStrings.DefaultConnection。\n" +
                $"当前基础路径: {basePath}");
        }

        optionsBuilder.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString),
            mysqlOptions =>
            {
                mysqlOptions.EnableRetryOnFailure(
                    maxRetryCount: 5,
                    maxRetryDelay: TimeSpan.FromSeconds(30),
                    errorNumbersToAdd: null);
            });

        return new AppDbContext(optionsBuilder.Options);
    }
}
