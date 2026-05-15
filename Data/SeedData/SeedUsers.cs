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
                        RoleId = 2, // 系统管理员角色
                        AppCode = "customer_001",
                        AllowedAreaIds = "1,2,3",
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
                        Phone = "13800138002",
                        Status = "active",
                        IsSuperAdmin = false,
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
                        Phone = "13800138003",
                        Status = "active",
                        IsSuperAdmin = false,
                        RoleId = 5, // 普通用户角色
                        AppCode = "customer_001",
                        AllowedAreaIds = "1,2",
                        LastLoginTime = null,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    },
                    // 厨师长 - 第二个租户
                    new User
                    {
                        Id = 5,
                        Username = "chef",
                        Password = HashPassword("chef123"),
                        FullName = "厨师长",
                        Email = "chef@greenenergy.com",
                        Phone = "13800138004",
                        Status = "active",
                        IsSuperAdmin = false,
                        RoleId = 6, // 厨师长角色
                        AppCode = "customer_002",
                        AllowedAreaIds = "1,2",
                        LastLoginTime = null,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    },
                    // 普通员工
                    new User
                    {
                        Id = 6,
                        Username = "staff",
                        Password = HashPassword("staff123"),
                        FullName = "普通员工",
                        Email = "staff@greenenergy.com",
                        Phone = "13800138005",
                        Status = "active",
                        IsSuperAdmin = false,
                        RoleId = 7, // 普通员工角色
                        AppCode = "customer_002",
                        AllowedAreaIds = "1",
                        LastLoginTime = null,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    }
                };

                await context.Users.AddRangeAsync(users);
                await context.SaveChangesAsync();

                _logger.LogInformation("成功初始化{Count}个用户", users.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "初始化用户种子数据时发生错误");
                throw;
            }
        }
    }
}