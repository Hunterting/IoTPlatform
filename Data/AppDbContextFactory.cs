using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

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
        
        // 使用默认连接字符串（可在 appsettings.json 中配置）
        var connectionString = "Server=localhost;Port=3306;Database=iot_platform;User=root;Password=root123;charset=utf8mb4;";
        
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
