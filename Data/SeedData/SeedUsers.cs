using IoTPlatform.Configuration;
using IoTPlatform.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace IoTPlatform.Data.SeedData
{
    /// <summary>
    /// 用户种子数据
    /// </summary>
    public class SeedUsers
    {
        private readonly ILogger<SeedUsers> _logger;

        public SeedUsers(ILogger<SeedUsers> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// 生成密码哈希
        /// </summary>
        private string HashPassword(string password)
        {
            //using var sha256 = SHA256.Create();
            //var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
            //return Convert.ToBase64String(bytes);
            return BCrypt.Net.BCrypt.HashPassword(password);
        }

        /// <summary>
        /// 初始化用户数据
        /// </summary>
        public async Task InitializeAsync(AppDbContext context)
        {
            try
            {
                _logger.LogInformation("开始初始化用户种子数据...");

                // 检查是否已有用户数据
                if (await context.Users.AnyAsync())
                {
                    _logger.LogInformation("数据库中已有用户数据，跳过初始化");
                    return;
                }

                // admin 单值列清空前的原始绑定快照：manager(Id=2) → customer_001(Id=1)。
                // 供 user_customers 幂等回填使用，保证「种子库」与「迁移库」同源（设计 §5.6）。
                var adminOriginalGrants = new Dictionary<long, long>
                {
                    [2] = 1
                };

                var users = new List<User>
                {
                    // 超级管理员 - 系统级账号，可切换任意租户
                    new User
                    {
                        Id = 1,
                        Username = "admin",
                        Password = HashPassword("admin123"),
                        FullName = "超级管理员",
                        Email = "admin@system.com",
                        Phone = "13800138000",
                        Status = "active",
                        IsSuperAdmin = true,
                        Role = "super_admin",
                        RoleId = 1, // 超级管理员角色
                        AppCode = "system",
                        AllowedAreaIds = null,
                        LastLoginTime = null,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    },
                    // 系统管理员 - 租户管理员账号
                    new User
                    {
                        Id = 2,
                        Username = "manager",
                        Password = HashPassword("admin123"),
                        FullName = "客户经理",
                        Email = "manager@haidilao.com",
                        Phone = "13800138001",
                        Status = "active",
                        IsSuperAdmin = false,
                        Role = "admin",
                        RoleId = 2, // 系统管理员角色
                        // 多客户授权模型（D7 / §5.6）：admin 单值列留空，
                        // 原绑定 customer_001(Id=1) 改由 user_customers 承载，见下方幂等回填。
                        CustomerId = null,
                        AppCode = null,
                        AllowedAreaIds = "1,2,3,15",
                        LastLoginTime = null,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    },
                    // 运维工程师
                    new User
                    {
                        Id = 3,
                        Username = "operator",
                        Password = HashPassword("operator123"),
                        FullName = "运维工程师",
                        Email = "operator@haidilao.com",
                        CustomerId = 1,
                        Phone = "13800138002",
                        Status = "active",
                        IsSuperAdmin = false,
                        Role = "operator",
                        RoleId = 4, // 运维工程师角色
                        AppCode = "customer_001",
                        AllowedAreaIds = "1",
                        LastLoginTime = null,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    },
                    // 普通用户
                    new User
                    {
                        Id = 4,
                        Username = "user1",
                        Password = HashPassword("user123"),
                        FullName = "普通用户",
                        Email = "user@haidilao.com",
                        CustomerId = 1,
                        Phone = "13800138003",
                        Status = "active",
                        IsSuperAdmin = false,
                        Role = "user",
                        RoleId = 5, // 普通用户角色
                        AppCode = "customer_001",
                        AllowedAreaIds = "1,2",
                        LastLoginTime = null,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    }
                };

                await context.Users.AddRangeAsync(users);
                await context.SaveChangesAsync();

                // 与迁移同源：清空 manager 单值列后，把原绑定写入授权中间表（source='seed'）。
                await SeedUserCustomerGrantsAsync(context, adminOriginalGrants);

                _logger.LogInformation("成功初始化{Count}个用户", users.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "初始化用户种子数据时发生错误");
                throw;
            }
        }

        /// <summary>
        /// 幂等回填 admin 的客户授权到 user_customers（设计 §5.6 种子侧）。
        /// <para>优先使用「清空单值列之前的原始绑定快照」；若处理存量库则回退按
        /// Admin.CustomerId / AppCode 反查客户。先查 user_customers 不存在再插，source='seed'。</para>
        /// </summary>
        private async Task SeedUserCustomerGrantsAsync(
            AppDbContext context,
            IReadOnlyDictionary<long, long> adminOriginalGrants)
        {
            // 种子运行在启动期，租户上下文可能残留；users/customers 实现了 IHasAppCode，
            // 必须绕过全局过滤器才能拿到全部 admin 与客户。user_customers 本身无过滤器。
            var admins = await context.Users
                .IgnoreQueryFilters()
                .Where(u => u.Role == "admin")
                .ToListAsync();

            var added = 0;
            foreach (var admin in admins)
            {
                long? customerId = null;
                if (adminOriginalGrants.TryGetValue(admin.Id, out var seededCustomerId))
                {
                    customerId = seededCustomerId;
                }
                else if (admin.CustomerId.HasValue)
                {
                    customerId = admin.CustomerId.Value;
                }
                else if (!string.IsNullOrEmpty(admin.AppCode))
                {
                    var appCode = admin.AppCode;
                    customerId = await context.Customers
                        .IgnoreQueryFilters()
                        .Where(c => c.AppCode == appCode)
                        .Select(c => (long?)c.Id)
                        .FirstOrDefaultAsync();
                }

                if (!customerId.HasValue)
                {
                    continue;
                }

                var exists = await context.UserCustomers
                    .AnyAsync(uc => uc.UserId == admin.Id && uc.CustomerId == customerId.Value);
                if (exists)
                {
                    continue;
                }

                context.UserCustomers.Add(new UserCustomer
                {
                    UserId = admin.Id,
                    CustomerId = customerId.Value,
                    GrantedByUserId = null,
                    Source = CustomerScopeConstants.SourceSeed,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                    RevokedAt = null
                });
                added++;
            }

            if (added > 0)
            {
                await context.SaveChangesAsync();
                _logger.LogInformation("成功回填{Count}条 admin 客户授权种子数据", added);
            }
            else
            {
                _logger.LogInformation("admin 客户授权种子数据已存在，跳过回填");
            }
        }
    }
}