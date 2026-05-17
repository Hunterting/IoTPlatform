using IoTPlatform.Data;
using IoTPlatform.DTOs.Requests;
using IoTPlatform.DTOs.Responses;
using IoTPlatform.Helpers;
using IoTPlatform.Models;
using IoTPlatform.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace IoTPlatform.Services;

/// <summary>
/// 受控设备服务实现
/// </summary>
public class ControlledDeviceService : IControlledDeviceService
{
    private readonly AppDbContext _db;
    private readonly ILogger<ControlledDeviceService> _logger;

    public ControlledDeviceService(AppDbContext db, ILogger<ControlledDeviceService> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ControlledDeviceDto> RegisterDeviceAsync(long deviceId, string? appCode, long? userId, string? userName)
    {
        // 1. 检查设备是否存在
        var device = await _db.Devices
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(d => d.Id == deviceId &&
                                      (appCode == null || d.AppCode == appCode));

        if (device == null)
        {
            throw new InvalidOperationException($"设备 {deviceId} 不存在或无权限访问");
        }

        // 2. 检查是否已注册
        var existing = await _db.ControlledDevices
            .FirstOrDefaultAsync(d => d.DeviceId == deviceId &&
                                     (appCode == null || d.AppCode == appCode));

        if (existing != null)
        {
            _logger.LogInformation("设备 {DeviceId} 已注册，跳过", deviceId);
            return MapToDto(existing, device);
        }

        // 3. 创建受控设备记录
        var controlledDevice = new ControlledDevice
        {
            AppCode = appCode,
            DeviceId = deviceId,
            DeviceName = device.Name,
            SerialNumber = device.SerialNumber,
            Model = device.Model,
            Category = device.Category,
            Location = device.Location,
            RegisteredAt = DateTime.UtcNow,
            CreatedBy = userId,
            CreatedByName = userName,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _db.ControlledDevices.Add(controlledDevice);
        await _db.SaveChangesAsync();

        _logger.LogInformation("设备 {DeviceId} ({DeviceName}) 已注册到控制系统", deviceId, device.Name);

        return MapToDto(controlledDevice, device);
    }

    /// <inheritdoc />
    public async Task<List<ControlledDeviceDto>> RegisterDevicesAsync(List<long> deviceIds, string? appCode, long? userId, string? userName)
    {
        var results = new List<ControlledDeviceDto>();

        foreach (var deviceId in deviceIds)
        {
            try
            {
                var result = await RegisterDeviceAsync(deviceId, appCode, userId, userName);
                results.Add(result);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "注册设备 {DeviceId} 失败", deviceId);
            }
        }

        return results;
    }

    /// <inheritdoc />
    public async Task<bool> UnregisterDeviceAsync(long id, string? appCode)
    {
        var query = _db.ControlledDevices.Where(d => d.Id == id);

        if (!string.IsNullOrEmpty(appCode))
            query = query.Where(d => d.AppCode == appCode);

        var device = await query.FirstOrDefaultAsync();
        if (device == null)
            return false;

        _db.ControlledDevices.Remove(device);
        await _db.SaveChangesAsync();

        _logger.LogInformation("受控设备 {Id} ({DeviceName}) 已取消注册", id, device.DeviceName);

        return true;
    }

    /// <inheritdoc />
    public async Task<ControlledDeviceDto?> UpdateDeviceAsync(long id, UpdateControlledDeviceRequest request, string? appCode)
    {
        var query = _db.ControlledDevices.Where(d => d.Id == id);

        if (!string.IsNullOrEmpty(appCode))
            query = query.Where(d => d.AppCode == appCode);

        var device = await query.FirstOrDefaultAsync();
        if (device == null)
            return null;

        // 更新字段
        if (request.Remark != null)
            device.Remark = request.Remark;

        if (request.Priority.HasValue)
            device.Priority = request.Priority.Value;

        if (request.IsEnabled.HasValue)
            device.IsEnabled = request.IsEnabled.Value;

        if (request.IsFavorite.HasValue)
            device.IsFavorite = request.IsFavorite.Value;

        device.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        _logger.LogInformation("受控设备 {Id} 已更新", id);

        return MapToDto(device);
    }

    /// <inheritdoc />
    public async Task<ControlledDeviceDto?> GetDeviceAsync(long id, string? appCode)
    {
        var query = _db.ControlledDevices
            .Include(d => d.Device)
            .Where(d => d.Id == id);

        if (!string.IsNullOrEmpty(appCode))
            query = query.Where(d => d.AppCode == appCode);

        var device = await query.FirstOrDefaultAsync();
        return device == null ? null : MapToDto(device);
    }

    /// <inheritdoc />
    public async Task<PagedResponse<ControlledDeviceDto>> GetDevicesAsync(
        string? appCode,
        int page,
        int pageSize,
        bool? isEnabled,
        bool? isFavorite)
    {
        var query = _db.ControlledDevices.Include(d => d.Device).AsQueryable();

        if (!string.IsNullOrEmpty(appCode))
            query = query.Where(d => d.AppCode == appCode);

        if (isEnabled.HasValue)
            query = query.Where(d => d.IsEnabled == isEnabled.Value);

        if (isFavorite.HasValue)
            query = query.Where(d => d.IsFavorite == isFavorite.Value);

        var total = await query.CountAsync();
        var items = await query
            .OrderByDescending(d => d.IsFavorite)
            .ThenByDescending(d => d.Priority)
            .ThenByDescending(d => d.RegisteredAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return PagedResponse<ControlledDeviceDto>.Create(
            items.Select(d => MapToDto(d)).ToList(),
            total, page, pageSize);
    }

    /// <inheritdoc />
    public async Task<bool> IsDeviceRegisteredAsync(long deviceId, string? appCode)
    {
        return await _db.ControlledDevices
            .AnyAsync(d => d.DeviceId == deviceId &&
                          (appCode == null || d.AppCode == appCode));
    }

    /// <inheritdoc />
    public async Task<bool> ToggleFavoriteAsync(long id, string? appCode)
    {
        var query = _db.ControlledDevices.Where(d => d.Id == id);

        if (!string.IsNullOrEmpty(appCode))
            query = query.Where(d => d.AppCode == appCode);

        var device = await query.FirstOrDefaultAsync();
        if (device == null)
            return false;

        device.IsFavorite = !device.IsFavorite;
        device.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        _logger.LogInformation("受控设备 {Id} 收藏状态已切换为 {IsFavorite}", id, device.IsFavorite);

        return true;
    }

    /// <inheritdoc />
    public async Task RecordCommandSentAsync(long id)
    {
        var device = await _db.ControlledDevices.FindAsync(id);
        if (device != null)
        {
            device.LastCommandAt = DateTime.UtcNow;
            device.CommandCount++;
            device.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }
    }

    /// <summary>
    /// 映射到DTO
    /// </summary>
    private static ControlledDeviceDto MapToDto(ControlledDevice entity, Device? device = null)
    {
        return new ControlledDeviceDto
        {
            Id = entity.Id,
            AppCode = entity.AppCode,
            DeviceId = entity.DeviceId,
            DeviceName = entity.DeviceName,
            SerialNumber = entity.SerialNumber,
            Model = entity.Model,
            Category = entity.Category,
            Location = entity.Location,
            Remark = entity.Remark,
            Priority = entity.Priority,
            IsEnabled = entity.IsEnabled,
            IsFavorite = entity.IsFavorite,
            RegisteredAt = entity.RegisteredAt,
            LastCommandAt = entity.LastCommandAt,
            CommandCount = entity.CommandCount,
            CreatedBy = entity.CreatedBy,
            CreatedByName = entity.CreatedByName,
            CreatedAt = entity.CreatedAt,
            UpdatedAt = entity.UpdatedAt,
            DeviceStatus = device?.Status ?? entity.Device?.Status
        };
    }
}
