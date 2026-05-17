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
    /// 字典项种子数据
    /// </summary>
    public class SeedDictionaries
    {
        private readonly ILogger<SeedDictionaries> _logger;

        public SeedDictionaries(ILogger<SeedDictionaries> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// 初始化字典项数据
        /// </summary>
        public async Task InitializeAsync(AppDbContext context)
        {
            try
            {
                _logger.LogInformation("开始初始化字典项种子数据...");

                // 检查是否已有字典项数据
                if (await context.DictionaryItems.AnyAsync())
                {
                    _logger.LogInformation("数据库中已有字典项数据，跳过初始化");
                    return;
                }

                // 确保字典类型已存在
                var types = await context.DictionaryTypeConfigs.ToListAsync();
                if (!types.Any())
                {
                    _logger.LogWarning("字典类型数据不存在，请先初始化字典类型");
                    return;
                }

                // 创建字典项并关联类型
                var dictionaries = new List<DictionaryItem>();

                // 设备类型字典
                dictionaries.AddRange(new[]
                {
                    new DictionaryItem
                    {
                        Type = "DeviceType",
                        TypeCode = "DeviceType",
                        Code = "temperature_sensor",
                        Name = "温度传感器",
                        Description = "温度监测传感器",
                        Sort = 1,
                        Status = "active",
                        AppCode = "system"
                    },
                    new DictionaryItem
                    {
                        Type = "DeviceType",
                        TypeCode = "DeviceType",
                        Code = "humidity_sensor",
                        Name = "湿度传感器",
                        Description = "湿度监测传感器",
                        Sort = 2,
                        Status = "active",
                        AppCode = "system"
                    },
                    new DictionaryItem
                    {
                        Type = "DeviceType",
                        TypeCode = "DeviceType",
                        Code = "pressure_sensor",
                        Name = "压力传感器",
                        Description = "压力监测传感器",
                        Sort = 3,
                        Status = "active",
                        AppCode = "system"
                    },
                    new DictionaryItem
                    {
                        Type = "DeviceType",
                        TypeCode = "DeviceType",
                        Code = "power_meter",
                        Name = "电能表",
                        Description = "电能计量设备",
                        Sort = 4,
                        Status = "active",
                        AppCode = "system"
                    }
                });

                // 设备状态字典
                dictionaries.AddRange(new[]
                {
                    new DictionaryItem
                    {
                        Type = "DeviceStatus",
                        TypeCode = "DeviceStatus",
                        Code = "online",
                        Name = "在线",
                        Description = "设备正常运行",
                        Sort = 1,
                        Status = "active",
                        AppCode = "system"
                    },
                    new DictionaryItem
                    {
                        Type = "DeviceStatus",
                        TypeCode = "DeviceStatus",
                        Code = "offline",
                        Name = "离线",
                        Description = "设备断开连接",
                        Sort = 2,
                        Status = "active",
                        AppCode = "system"
                    },
                    new DictionaryItem
                    {
                        Type = "DeviceStatus",
                        TypeCode = "DeviceStatus",
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
                        TypeCode = "DeviceStatus",
                        Code = "maintenance",
                        Name = "维护中",
                        Description = "设备正在维护",
                        Sort = 4,
                        Status = "active",
                        AppCode = "system"
                    }
                });

                // 告警级别字典
                dictionaries.AddRange(new[]
                {
                    new DictionaryItem
                    {
                        Type = "AlertLevel",
                        TypeCode = "AlertLevel",
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
                        TypeCode = "AlertLevel",
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
                        TypeCode = "AlertLevel",
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
                        TypeCode = "AlertLevel",
                        Code = "emergency",
                        Name = "紧急",
                        Description = "紧急级别告警",
                        Sort = 4,
                        Status = "active",
                        AppCode = "system"
                    }
                });

                // 工单状态字典
                dictionaries.AddRange(new[]
                {
                    new DictionaryItem
                    {
                        Type = "WorkOrderStatus",
                        TypeCode = "WorkOrderStatus",
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
                        TypeCode = "WorkOrderStatus",
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
                        TypeCode = "WorkOrderStatus",
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
                        TypeCode = "WorkOrderStatus",
                        Code = "closed",
                        Name = "已关闭",
                        Description = "工单已关闭",
                        Sort = 4,
                        Status = "active",
                        AppCode = "system"
                    }
                });

                // 档案分类字典
                dictionaries.AddRange(new[]
                {
                    new DictionaryItem
                    {
                        Type = "ArchiveCategory",
                        TypeCode = "ArchiveCategory",
                        Code = "blueprints",
                        Name = "图纸资料",
                        Description = "建筑图纸、设计图等",
                        Sort = 1,
                        Status = "active",
                        AppCode = "system"
                    },
                    new DictionaryItem
                    {
                        Type = "ArchiveCategory",
                        TypeCode = "ArchiveCategory",
                        Code = "equipment_manuals",
                        Name = "设备手册",
                        Description = "设备使用手册、维护手册等",
                        Sort = 2,
                        Status = "active",
                        AppCode = "system"
                    },
                    new DictionaryItem
                    {
                        Type = "ArchiveCategory",
                        TypeCode = "ArchiveCategory",
                        Code = "maintenance_records",
                        Name = "维护记录",
                        Description = "设备维护记录、保养记录等",
                        Sort = 3,
                        Status = "active",
                        AppCode = "system"
                    },
                    new DictionaryItem
                    {
                        Type = "ArchiveCategory",
                        TypeCode = "ArchiveCategory",
                        Code = "safety_docs",
                        Name = "安全文档",
                        Description = "安全操作规程、应急预案等",
                        Sort = 4,
                        Status = "active",
                        AppCode = "system"
                    },
                    new DictionaryItem
                    {
                        Type = "ArchiveCategory",
                        TypeCode = "ArchiveCategory",
                        Code = "contracts",
                        Name = "合同协议",
                        Description = "采购合同、服务协议等",
                        Sort = 5,
                        Status = "active",
                        AppCode = "system"
                    },
                    new DictionaryItem
                    {
                        Type = "ArchiveCategory",
                        TypeCode = "ArchiveCategory",
                        Code = "inspection_reports",
                        Name = "检测报告",
                        Description = "设备检测报告、验收报告等",
                        Sort = 6,
                        Status = "active",
                        AppCode = "system"
                    },
                    new DictionaryItem
                    {
                        Type = "ArchiveCategory",
                        TypeCode = "ArchiveCategory",
                        Code = "air_quality",
                        Name = "空气质量",
                        Description = "空气质量监测数据、分析报告等",
                        Sort = 7,
                        Status = "active",
                        AppCode = "system"
                    },
                    new DictionaryItem
                    {
                        Type = "ArchiveCategory",
                        TypeCode = "ArchiveCategory",
                        Code = "video_monitoring",
                        Name = "视频监控",
                        Description = "视频监控录像、监控截图等",
                        Sort = 8,
                        Status = "active",
                        AppCode = "system"
                    }
                });

                await context.DictionaryItems.AddRangeAsync(dictionaries);
                await context.SaveChangesAsync();

                _logger.LogInformation("成功初始化 {Count} 个字典项", dictionaries.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "初始化字典项种子数据时发生错误");
                throw;
            }
        }
    }
}
