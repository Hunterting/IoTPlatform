using IoTPlatform.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace IoTPlatform.Data.SeedData
{
    /// <summary>
    /// 客户种子数据
    /// </summary>
    public class SeedCustomers
    {
        private readonly ILogger<SeedCustomers> _logger;

        public SeedCustomers(ILogger<SeedCustomers> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// 初始化客户数据
        /// </summary>
        public async Task InitializeAsync(AppDbContext context)
        {
            try
            {
                _logger.LogInformation("开始初始化客户种子数据...");

                // 检查是否已有客户数据
                if (await context.Customers.AnyAsync())
                {
                    _logger.LogInformation("数据库中已有客户数据，跳过初始化");
                    return;
                }

                var customers = new List<Customer>
                {
                    new Customer
                    {
                        Id = 1,
                        Name = "智慧工厂有限公司",
                        Code = "customer_001",
                        ContactPerson = "张经理",
                        ContactPhone = "13800138000",
                        ContactEmail = "zhang@smartfactory.com",
                        Address = "北京市海淀区中关村科技园1号",
                        Industry = "智能制造",
                        Status = true,
                        AppCode = "customer_001",
                        LicenseKey = "LIC-001-2024-001",
                        LicenseExpireDate = DateTime.UtcNow.AddYears(1),
                        MaxDeviceCount = 1000,
                        MaxUserCount = 50,
                        MaxAreaCount = 10,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    },
                    new Customer
                    {
                        Id = 2,
                        Name = "绿色能源集团",
                        Code = "customer_002",
                        ContactPerson = "李总监",
                        ContactPhone = "13800138001",
                        ContactEmail = "li@greenenergy.com",
                        Address = "上海市浦东新区张江高科技园区",
                        Industry = "新能源",
                        Status = true,
                        AppCode = "customer_002",
                        LicenseKey = "LIC-002-2024-002",
                        LicenseExpireDate = DateTime.UtcNow.AddYears(1),
                        MaxDeviceCount = 500,
                        MaxUserCount = 30,
                        MaxAreaCount = 5,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    },
                    new Customer
                    {
                        Id = 3,
                        Name = "智慧建筑科技",
                        Code = "customer_003",
                        ContactPerson = "王总",
                        ContactPhone = "13800138002",
                        ContactEmail = "wang@smartbuilding.com",
                        Address = "深圳市南山区科技园",
                        Industry = "建筑智能化",
                        Status = true,
                        AppCode = "customer_003",
                        LicenseKey = "LIC-003-2024-003",
                        LicenseExpireDate = DateTime.UtcNow.AddYears(1),
                        MaxDeviceCount = 200,
                        MaxUserCount = 20,
                        MaxAreaCount = 3,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    }
                };

                await context.Customers.AddRangeAsync(customers);
                await context.SaveChangesAsync();

                _logger.LogInformation("成功初始化{Count}个客户", customers.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "初始化客户种子数据时发生错误");
                throw;
            }
        }
    }
}