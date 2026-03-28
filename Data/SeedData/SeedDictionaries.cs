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
    /// 数据字典种子数据
    /// </summary>
    public class SeedDictionaries
    {
        private readonly ILogger<SeedDictionaries> _logger;

        public SeedDictionaries(ILogger<SeedDictionaries> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// 初始化字典数据
        /// </summary>
        public async Task InitializeAsync(AppDbContext context)
        {
            try
            {
                _logger.LogInformation("开始初始化数据字典种子数据...");

                // 检查是否已有字典数据
                if (await context.Dictionaries.AnyAsync())
                {
                    _logger.LogInformation("数据库中已有字典数据，跳过初始化");
                    return;
                }

                var dictionaries = new List<Dictionary>
                {
                    // 设备类型字典
                    new Dictionary
                    {
                        Id = 1,
                        Type = "DeviceType",
                        Code = "temperature_sensor",
                        Name = "温度传感器",
                        Value = "1",
                        Description = "用于测量环境温度的传感器",
                        Sort = 1,
                        Status = true,
                        AppCode = "system",
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    },
                    new Dictionary
                    {
                        Id = 2,
                        Type = "DeviceType",
                        Code = "humidity_sensor",
                        Name = "湿度传感器",
                        Value = "2",
                        Description = "用于测量环境湿度的传感器",
                        Sort = 2,
                        Status = true,
                        AppCode = "system",
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    },
                    new Dictionary
                    {
                        Id = 3,
                        Type = "DeviceType",
                        Code = "pressure_sensor",
                        Name = "压力传感器",
                        Value = "3",
                        Description = "用于测量气体或液体压力的传感器",
                        Sort = 3,
                        Status = true,
                        AppCode = "system",
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    },
                    new Dictionary
                    {
                        Id = 4,
                        Type = "DeviceType",
                        Code = "power_meter",
                        Name = "电能表",
                        Value = "4",
                        Description = "用于测量电能消耗的电能表",
                        Sort = 4,
                        Status = true,
                        AppCode = "system",
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    },

                    // 设备状态字典
                    new Dictionary
                    {
                        Id = 5,
                        Type = "DeviceStatus",
                        Code = "online",
                        Name = "在线",
                        Value = "1",
                        Description = "设备正常在线",
                        Sort = 1,
                        Status = true,
                        AppCode = "system",
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    },
                    new Dictionary
                    {
                        Id = 6,
                        Type = "DeviceStatus",
                        Code = "offline",
                        Name = "离线",
                        Value = "2",
                        Description = "设备离线",
                        Sort = 2,
                        Status = true,
                        AppCode = "system",
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    },
                    new Dictionary
                    {
                        Id = 7,
                        Type = "DeviceStatus",
                        Code = "fault",
                        Name = "故障",
                        Value = "3",
                        Description = "设备故障",
                        Sort = 3,
                        Status = true,
                        AppCode = "system",
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    },
                    new Dictionary
                    {
                        Id = 8,
                        Type = "DeviceStatus",
                        Code = "maintenance",
                        Name = "维护中",
                        Value = "4",
                        Description = "设备正在维护",
                        Sort = 4,
                        Status = true,
                        AppCode = "system",
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    },

                    // 告警级别字典
                    new Dictionary
                    {
                        Id = 9,
                        Type = "AlertLevel",
                        Code = "info",
                        Name = "信息",
                        Value = "1",
                        Description = "信息级别告警",
                        Sort = 1,
                        Status = true,
                        AppCode = "system",
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    },
                    new Dictionary
                    {
                        Id = 10,
                        Type = "AlertLevel",
                        Code = "warning",
                        Name = "警告",
                        Value = "2",
                        Description = "警告级别告警",
                        Sort = 2,
                        Status = true,
                        AppCode = "system",
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    },
                    new Dictionary
                    {
                        Id = 11,
                        Type = "AlertLevel",
                        Code = "critical",
                        Name = "严重",
                        Value = "3",
                        Description = "严重级别告警",
                        Sort = 3,
                        Status = true,
                        AppCode = "system",
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    },
                    new Dictionary
                    {
                        Id = 12,
                        Type = "AlertLevel",
                        Code = "emergency",
                        Name = "紧急",
                        Value = "4",
                        Description = "紧急级别告警",
                        Sort = 4,
                        Status = true,
                        AppCode = "system",
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    },

                    // 工单状态字典
                    new Dictionary
                    {
                        Id = 13,
                        Type = "WorkOrderStatus",
                        Code = "pending",
                        Name = "待处理",
                        Value = "1",
                        Description = "工单待处理",
                        Sort = 1,
                        Status = true,
                        AppCode = "system",
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    },
                    new Dictionary
                    {
                        Id = 14,
                        Type = "WorkOrderStatus",
                        Code = "processing",
                        Name = "处理中",
                        Value = "2",
                        Description = "工单处理中",
                        Sort = 2,
                        Status = true,
                        AppCode = "system",
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    },
                    new Dictionary
                    {
                        Id = 15,
                        Type = "WorkOrderStatus",
                        Code = "resolved",
                        Name = "已解决",
                        Value = "3",
                        Description = "工单已解决",
                        Sort = 3,
                        Status = true,
                        AppCode = "system",
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    },
                    new Dictionary
                    {
                        Id = 16,
                        Type = "WorkOrderStatus",
                        Code = "closed",
                        Name = "已关闭",
                        Value = "4",
                        Description = "工单已关闭",
                        Sort = 4,
                        Status = true,
                        AppCode = "system",
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    }
                };

                await context.Dictionaries.AddRangeAsync(dictionaries);
                await context.SaveChangesAsync();

                _logger.LogInformation("成功初始化{Count}个字典项", dictionaries.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "初始化数据字典种子数据时发生错误");
                throw;
            }
        }
    }
}