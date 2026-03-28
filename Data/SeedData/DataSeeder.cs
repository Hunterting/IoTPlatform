using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;

namespace IoTPlatform.Data.SeedData
{
    /// <summary>
    /// 数据种子初始化器
    /// </summary>
    public class DataSeeder
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<DataSeeder> _logger;

        public DataSeeder(IServiceProvider serviceProvider, ILogger<DataSeeder> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        /// <summary>
        /// 初始化所有种子数据
        /// </summary>
        public async Task InitializeAllAsync()
        {
            _logger.LogInformation("开始初始化所有种子数据...");

            using var scope = _serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            try
            {
                // 确保数据库已创建
                await context.Database.EnsureCreatedAsync();
                _logger.LogInformation("数据库确认完成");

                // 按顺序初始化种子数据
                var seedRoles = new SeedRoles(
                    _serviceProvider.GetRequiredService<ILogger<SeedRoles>>()
                );
                await seedRoles.InitializeAsync(context);

                var seedCustomers = new SeedCustomers(
                    _serviceProvider.GetRequiredService<ILogger<SeedCustomers>>()
                );
                await seedCustomers.InitializeAsync(context);

                var seedUsers = new SeedUsers(
                    _serviceProvider.GetRequiredService<ILogger<SeedUsers>>()
                );
                await seedUsers.InitializeAsync(context);

                // 字典初始化
                var seedDictionaries = new SeedDictionaries(
                    _serviceProvider.GetRequiredService<ILogger<SeedDictionaries>>()
                );
                await seedDictionaries.InitializeAsync(context);

                _logger.LogInformation("所有种子数据初始化完成");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "初始化种子数据时发生错误");
                throw;
            }
        }

        /// <summary>
        /// 开发环境专用初始化方法
        /// </summary>
        public async Task InitializeForDevelopmentAsync()
        {
            _logger.LogInformation("开始开发环境种子数据初始化...");

            using var scope = _serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            try
            {
                // 确保数据库已创建
                await context.Database.EnsureCreatedAsync();

                // 清空现有数据（仅开发环境）
                await ClearDatabaseAsync(context);

                // 初始化所有种子数据
                await InitializeAllAsync();

                // 添加额外的开发测试数据
                await AddDevelopmentTestDataAsync(context);

                _logger.LogInformation("开发环境种子数据初始化完成");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "初始化开发环境种子数据时发生错误");
                throw;
            }
        }

        /// <summary>
        /// 清空数据库（仅用于开发和测试）
        /// </summary>
        private async Task ClearDatabaseAsync(AppDbContext context)
        {
            _logger.LogWarning("正在清空数据库表（仅开发环境）...");

            // 按依赖关系顺序删除数据
            context.Users.RemoveRange(context.Users);
            context.Roles.RemoveRange(context.Roles);
            context.Customers.RemoveRange(context.Customers);
            context.DictionaryItems.RemoveRange(context.DictionaryItems);

            await context.SaveChangesAsync();

            _logger.LogWarning("数据库清空完成");
        }

        /// <summary>
        /// 添加开发测试数据
        /// </summary>
        private async Task AddDevelopmentTestDataAsync(AppDbContext context)
        {
            _logger.LogInformation("正在添加开发测试数据...");

            // 这里可以添加更多的测试数据
            // 例如：测试设备、测试区域、测试告警记录等

            _logger.LogInformation("开发测试数据添加完成");
        }
    }
}