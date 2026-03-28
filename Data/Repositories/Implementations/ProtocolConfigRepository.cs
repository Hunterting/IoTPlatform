using IoTPlatform.Data.Repositories.Interfaces;
using IoTPlatform.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace IoTPlatform.Data.Repositories.Implementations;

/// <summary>
/// 协议配置仓储实现类
/// </summary>
public class ProtocolConfigRepository : Repository<ProtocolConfig>, IProtocolConfigRepository
{
    public ProtocolConfigRepository(AppDbContext context) : base(context)
    {
    }

    public async Task<IEnumerable<ProtocolConfig>> GetByTypeAsync(string type, string? appCode = null)
    {
        var query = ApplyFilters(_context.ProtocolConfigs.AsQueryable(), appCode, null);
        return await query
            .Where(p => p.Type == type)
            .ToListAsync();
    }

    public async Task<IEnumerable<ProtocolConfig>> GetByStatusAsync(string status, string? appCode = null)
    {
        var query = ApplyFilters(_context.ProtocolConfigs.AsQueryable(), appCode, null);
        return await query
            .Where(p => p.Status == status)
            .ToListAsync();
    }

    public async Task<IEnumerable<ProtocolConfig>> GetByDeviceIdAsync(long deviceId, string? appCode = null)
    {
        var query = ApplyFilters(_context.ProtocolConfigs.AsQueryable(), appCode, null);
        var allConfigs = await query.ToListAsync();
        
        // 筛选包含指定设备ID的配置
        return allConfigs.Where(p => 
            string.IsNullOrEmpty(p.DeviceIds) || 
            (JsonSerializer.Deserialize<List<long>>(p.DeviceIds ?? "[]")?.Contains(deviceId) ?? false)
        );
    }

    public async Task<IEnumerable<ProtocolConfig>> GetActiveConfigsAsync(string? appCode = null)
    {
        var query = ApplyFilters(_context.ProtocolConfigs.AsQueryable(), appCode, null);
        return await query
            .Where(p => p.Status == "active")
            .ToListAsync();
    }

    public async Task UpdateStatusAsync(long configId, string status)
    {
        var config = await _context.ProtocolConfigs.FindAsync(configId);
        if (config != null)
        {
            config.Status = status;
            config.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }
    }

    public async Task<bool> NameExistsAsync(string name, string? appCode = null, long? excludeConfigId = null)
    {
        var query = ApplyFilters(_context.ProtocolConfigs.AsQueryable(), appCode, null);
        
        query = query.Where(p => p.Name == name);
        
        if (excludeConfigId.HasValue)
        {
            query = query.Where(p => p.Id != excludeConfigId.Value);
        }
        
        return await query.AnyAsync();
    }

    public async Task<ProtocolStats> GetProtocolStatsAsync(string? appCode = null)
    {
        var query = ApplyFilters(_context.ProtocolConfigs.AsQueryable(), appCode, null);
        var configs = await query.ToListAsync();
        
        var stats = new ProtocolStats
        {
            TotalConfigs = configs.Count,
            ActiveConfigs = configs.Count(p => p.Status == "active"),
            InactiveConfigs = configs.Count(p => p.Status == "inactive"),
            ModbusConfigs = configs.Count(p => p.Type == "modbus"),
            MqttConfigs = configs.Count(p => p.Type == "mqtt"),
            OpcUaConfigs = configs.Count(p => p.Type == "opc_ua"),
            HttpConfigs = configs.Count(p => p.Type == "http"),
            TcpConfigs = configs.Count(p => p.Type == "tcp"),
            BacnetConfigs = configs.Count(p => p.Type == "bacnet")
        };
        
        return stats;
    }
}