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
            using var sha256 = SHA256.Create();
            var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
            return Convert.ToBase64String(bytes);
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
                    new User
                    {
                        Id = 1,
                        Username = "admin",
                        Password = HashPassword("Admin@123"), // 默认密码
                        FullName = "系统管理员",
                        Email = "admin@iotplatform.com",
                        Phone = "13800138000",
                        Status = true,
                        IsSuperAdmin = true,
                        RoleId = 1, // 超级管理员角色
                        AppCode = "system",
                        AllowedAreaIds = null, // 超级管理员可以访问所有区域
                        LastLoginTime = null,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    },
                    new User
                    {
                        Id = 2,
                        Username = "manager",
                        Password = HashPassword("Manager@123"),
                        FullName = "客户经理",
                        Email = "manager@iotplatform.com",
                        Phone = "13800138001",
                        Status = true,
                        IsSuperAdmin = false,
                        RoleId = 2, // 系统管理员角色
                        AppCode = "customer_001",
                        AllowedAreaIds = "1,2,3", // 可以访问区域1,2,3
                        LastLoginTime = null,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    },
                    new User
                    {
                        Id = 3,
                        Username = "operator1",
                        Password = HashPassword("Operator@123"),
                        FullName = "运维工程师1",
                        Email = "operator1@iotplatform.com",
                        Phone = "13800138002",
                        Status = true,
                        IsSuperAdmin = false,
                        RoleId = 4, // 运维工程师角色
                        AppCode = "customer_001",
                        AllowedAreaIds = "1", // 只能访问区域1
                        LastLoginTime = null,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    },
                    new User
                    {
                        Id = 4,
                        Username = "user1",
                        Password = HashPassword("User@123"),
                        FullName = "普通用户1",
                        Email = "user1@iotplatform.com",
                        Phone = "13800138003",
                        Status = true,
                        IsSuperAdmin = false,
                        RoleId = 5, // 普通用户角色
                        AppCode = "customer_001",
                        AllowedAreaIds = "1,2", // 可以访问区域1和2
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