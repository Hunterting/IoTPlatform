using IoTPlatform.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace IoTPlatform.Data.SeedData
{
    /// <summary>
    /// 角色种子数据
    /// </summary>
    public class SeedRoles
    {
        private readonly ILogger<SeedRoles> _logger;

        public SeedRoles(ILogger<SeedRoles> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// 初始化角色数据
        /// </summary>
        public async Task InitializeAsync(AppDbContext context)
        {
            try
            {
                _logger.LogInformation("开始初始化角色种子数据...");

                // 检查是否已有角色数据
                if (await context.Roles.AnyAsync())
                {
                    _logger.LogInformation("数据库中已有角色数据，跳过初始化");
                    return;
                }

                var roles = new List<Role>
                {
                    new Role
                    {
                        Id = 1,
                        Name = "超级管理员",
                        Code = "super_admin",
                        Description = "系统超级管理员，拥有所有权限",
                        IsSystem = true,
                        IsDefault = false,
                        Status = "active",
                        AppCode = "system",
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    },
                    new Role
                    {
                        Id = 2,
                        Name = "系统管理员",
                        Code = "admin",
                        Description = "系统管理员，拥有大部分管理权限",
                        IsSystem = true,
                        IsDefault = true,
                        Status = "active",
                        AppCode = "system",
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    },
                    new Role
                    {
                        Id = 3,
                        Name = "客户管理员",
                        Code = "customer_admin",
                        Description = "客户管理员，管理特定客户的数据",
                        IsSystem = false,
                        IsDefault = false,
                        Status = "active",
                        AppCode = "system",
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    },
                    new Role
                    {
                        Id = 4,
                        Name = "运维工程师",
                        Code = "operator",
                        Description = "运维工程师，负责设备监控和维护",
                        IsSystem = false,
                        IsDefault = false,
                        Status = "active",
                        AppCode = "system",
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    },
                    new Role
                    {
                        Id = 5,
                        Name = "普通用户",
                        Code = "user",
                        Description = "普通用户，拥有基本查看权限",
                        IsSystem = false,
                        IsDefault = false,
                        Status = "active",
                        AppCode = "system",
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    }
                };

                await context.Roles.AddRangeAsync(roles);
                await context.SaveChangesAsync();

                _logger.LogInformation("成功初始化{Count}个角色", roles.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "初始化角色种子数据时发生错误");
                throw;
            }
        }
    }
}