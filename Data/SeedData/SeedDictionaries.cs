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
                if (await context.DictionaryItems.AnyAsync())
                {
                    _logger.LogInformation("数据库中已有字典数据，跳过初始化");
                    return;
                }

                var dictionaries = new List<DictionaryItem>
                {
                    // 设备类型字典
                    new DictionaryItem
                    {
                        Type = "DeviceType",
                        Code = "temperature_sensor",
                        Name = "温度传感器",
                        Sort = 1,
                        Status = "active",
                        AppCode = "system"
                    },
                    new DictionaryItem
                    {
                        Type = "DeviceType",
                        Code = "humidity_sensor",
                        Name = "湿度传感器",
                        Sort = 2,
                        Status = "active",
                        AppCode = "system"
                    },
                    new DictionaryItem
                    {
                        Type = "DeviceType",
                        Code = "pressure_sensor",
                        Name = "压力传感器",
                        Sort = 3,
                        Status = "active",
                        AppCode = "system"
                    },
                    new DictionaryItem
                    {
                        Type = "DeviceType",
                        Code = "power_meter",
                        Name = "电能表",
                        Sort = 4,
                        Status = "active",
                        AppCode = "system"
                    },

                    // 设备状态字典
                    new DictionaryItem
                    {
                        Type = "DeviceStatus",
                        Code = "online",
                        Name = "在线",
                        Sort = 1,
                        Status = "active",
                        AppCode = "system"
                    },
                    new DictionaryItem
                    {
                        Type = "DeviceStatus",
                        Code = "offline",
                        Name = "离线",
                        Sort = 2,
                        Status = "active",
                        AppCode = "system"
                    },
                    new DictionaryItem
                    {
                        Type = "DeviceStatus",
                        Code = "fault",
                        Name = "故障",
                        Description = "设备故障",
                        Sort = 3,
                        Status = "active",
                        AppCode = "system"
                    },
                    new DictionaryItem
                    {
                        Type = "DeviceStatus",
                        Code = "maintenance",
                        Name = "维护中",
                        Description = "设备正在维护",
                        Sort = 4,
                        Status = "active",
                        AppCode = "system"
                    },

                    // 告警级别字典
                    new DictionaryItem
                    {
                        Type = "AlertLevel",
                        Code = "info",
                        Name = "信息",
                        Description = "信息级别告警",
                        Sort = 1,
                        Status = "active",
                        AppCode = "system"
                    },
                    new DictionaryItem
                    {
                        Type = "AlertLevel",
                        Code = "warning",
                        Name = "警告",
                        Description = "警告级别告警",
                        Sort = 2,
                        Status = "active",
                        AppCode = "system"
                    },
                    new DictionaryItem
                    {
                        Type = "AlertLevel",
                        Code = "critical",
                        Name = "严重",
                        Description = "严重级别告警",
                        Sort = 3,
                        Status = "active",
                        AppCode = "system"
                    },
                    new DictionaryItem
                    {
                        Type = "AlertLevel",
                        Code = "emergency",
                        Name = "紧急",
                        Description = "紧急级别告警",
                        Sort = 4,
                        Status = "active",
                        AppCode = "system"
                    },

                    // 工单状态字典
                    new DictionaryItem
                    {
                        Type = "WorkOrderStatus",
                        Code = "pending",
                        Name = "待处理",
                        Description = "工单待处理",
                        Sort = 1,
                        Status = "active",
                        AppCode = "system"
                    },
                    new DictionaryItem
                    {
                        Type = "WorkOrderStatus",
                        Code = "processing",
                        Name = "处理中",
                        Description = "工单处理中",
                        Sort = 2,
                        Status = "active",
                        AppCode = "system"
                    },
                    new DictionaryItem
                    {
                        Type = "WorkOrderStatus",
                        Code = "resolved",
                        Name = "已解决",
                        Description = "工单已解决",
                        Sort = 3,
                        Status = "active",
                        AppCode = "system"
                    },
                    new DictionaryItem
                    {
                        Type = "WorkOrderStatus",
                        Code = "closed",
                        Name = "已关闭",
                        Description = "工单已关闭",
                        Sort = 4,
                        Status = "active",
                        AppCode = "system"
                    }
                };

                await context.DictionaryItems.AddRangeAsync(dictionaries);
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
