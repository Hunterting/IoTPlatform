using IoTPlatform.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace IoTPlatform.Data.SeedData
{
    /// <summary>
    /// 字典类型种子数据
    /// </summary>
    public class SeedDictionaryTypes
    {
        private readonly ILogger<SeedDictionaryTypes> _logger;

        public SeedDictionaryTypes(ILogger<SeedDictionaryTypes> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// 初始化字典类型数据
        /// </summary>
        public async Task InitializeAsync(AppDbContext context)
        {
            try
            {
                _logger.LogInformation("开始初始化字典类型种子数据...");

                // 检查是否已有字典类型数据
                if (await context.DictionaryTypeConfigs.AnyAsync())
                {
                    _logger.LogInformation("数据库中已有字典类型数据，跳过初始化");
                    return;
                }

                var dictionaryTypes = new List<DictionaryTypeConfig>
                {
                    // 设备类型
                    new DictionaryTypeConfig
                    {
                        Code = "DeviceType",
                        Name = "设备类型",
                        Description = "物联网设备类型分类",
                        SortOrder = 1,
                        IsActive = true,
                        AppCode = "system"
                    },

                    // 设备状态
                    new DictionaryTypeConfig
                    {
                        Code = "DeviceStatus",
                        Name = "设备状态",
                        Description = "设备运行状态",
                        SortOrder = 2,
                        IsActive = true,
                        AppCode = "system"
                    },

                    // 告警级别
                    new DictionaryTypeConfig
                    {
                        Code = "AlertLevel",
                        Name = "告警级别",
                        Description = "告警信息严重程度",
                        SortOrder = 3,
                        IsActive = true,
                        AppCode = "system"
                    },

                    // 工单状态
                    new DictionaryTypeConfig
                    {
                        Code = "WorkOrderStatus",
                        Name = "工单状态",
                        Description = "工单处理状态",
                        SortOrder = 4,
                        IsActive = true,
                        AppCode = "system"
                    },

                    // 档案分类
                    new DictionaryTypeConfig
                    {
                        Code = "ArchiveCategory",
                        Name = "档案分类",
                        Description = "档案资料分类",
                        SortOrder = 5,
                        IsActive = true,
                        AppCode = "system"
                    },

                    // 设备类别
                    new DictionaryTypeConfig
                    {
                        Code = "DeviceCategory",
                        Name = "设备类别",
                        Description = "设备分类",
                        SortOrder = 6,
                        IsActive = true,
                        AppCode = "system"
                    },

                    // 设备品牌
                    new DictionaryTypeConfig
                    {
                        Code = "DeviceBrand",
                        Name = "设备品牌",
                        Description = "设备品牌厂商",
                        SortOrder = 7,
                        IsActive = true,
                        AppCode = "system"
                    },

                    // 工单类型
                    new DictionaryTypeConfig
                    {
                        Code = "WorkOrderType",
                        Name = "工单类型",
                        Description = "工单类型分类",
                        SortOrder = 8,
                        IsActive = true,
                        AppCode = "system"
                    },

                    // 工单优先级
                    new DictionaryTypeConfig
                    {
                        Code = "WorkOrderPriority",
                        Name = "工单优先级",
                        Description = "工单处理优先级",
                        SortOrder = 9,
                        IsActive = true,
                        AppCode = "system"
                    },

                    // 传感器类型
                    new DictionaryTypeConfig
                    {
                        Code = "SensorType",
                        Name = "传感器类型",
                        Description = "传感器类型分类",
                        SortOrder = 10,
                        IsActive = true,
                        AppCode = "system"
                    }
                };

                await context.DictionaryTypeConfigs.AddRangeAsync(dictionaryTypes);
                await context.SaveChangesAsync();

                _logger.LogInformation("成功初始化 {Count} 个字典类型", dictionaryTypes.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "初始化字典类型种子数据时发生错误");
                throw;
            }
        }
    }
}
