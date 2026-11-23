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

                // manager(Id=2) 的授权客户集合：customer_001(Id=1)、customer_002(Id=2)、
                // customer_003(Id=3)。供 user_customers 幂等回填使用，保证「种子库」与
                // 「迁移库」同源（设计 §5.6）；不再只是单值列清空前的原始绑定快照。
                var adminOriginalGrants = new Dictionary<long, long[]>
                {
                    [2] = new long[] { 1, 2, 3 }
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

                // 与迁移同源：清空 manager 单值列后，把其授权客户集合写入授权中间表（source='seed'）。
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
        /// <para>优先按 adminOriginalGrants 中的「授权客户集合」逐个回填（manager →
        /// customer_001/002/003）；快照缺失或为空数组时回退按 Admin.CustomerId / AppCode
        /// 反查客户，以兼容存量库。每条均先查 user_customers 不存在再插，source='seed'。</para>
        /// </summary>
        private async Task SeedUserCustomerGrantsAsync(
            AppDbContext context,
            IReadOnlyDictionary<long, long[]> adminOriginalGrants)
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
                // 1) 种子快照优先：一个 admin 可对应多个授权客户，逐个幂等回填。
                adminOriginalGrants.TryGetValue(admin.Id, out var seededCustomerIds);
                if (seededCustomerIds != null && seededCustomerIds.Length > 0)
                {
                    foreach (var seededCustomerId in seededCustomerIds)
                    {
                        if (await GrantIfAbsentAsync(context, admin.Id, seededCustomerId))
                        {
                            added++;
                        }
                    }

                    continue;
                }

                // 2) 快照缺失或为空数组：回退存量库兼容逻辑，按单值 CustomerId / AppCode 反查。
                long? customerId = null;
                if (admin.CustomerId.HasValue)
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

                if (customerId.HasValue && await GrantIfAbsentAsync(context, admin.Id, customerId.Value))
                {
                    added++;
                }
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

        /// <summary>
        /// 幂等插入单条 user_customers 授权：已存在则跳过，返回是否实际新增。
        /// </summary>
        private async Task<bool> GrantIfAbsentAsync(AppDbContext context, long adminId, long customerId)
        {
            var exists = await context.UserCustomers
                .AnyAsync(uc => uc.UserId == adminId && uc.CustomerId == customerId);
            if (exists)
            {
                return false;
            }

            context.UserCustomers.Add(new UserCustomer
            {
                UserId = adminId,
                CustomerId = customerId,
                GrantedByUserId = null,
                Source = CustomerScopeConstants.SourceSeed,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                RevokedAt = null
            });
            return true;
        }
    }
}