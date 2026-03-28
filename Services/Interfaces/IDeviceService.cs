using IoTPlatform.DTOs.Requests;
using IoTPlatform.DTOs.Responses;
using IoTPlatform.Helpers;

namespace IoTPlatform.Services;

/// <summary>
/// 设备服务接口
/// </summary>
public interface IDeviceService
{
    Task<PagedResponse<DeviceDto>> GetDevicesAsync(int page, int pageSize, string? keyword, string? status, string? appCode, List<long>? allowedAreaIds);
    Task<DeviceDto?> GetDeviceAsync(long id, string? appCode, List<long>? allowedAreaIds);
    Task<DeviceDto> CreateDeviceAsync(CreateDeviceRequest request);
    Task<DeviceDto> UpdateDeviceAsync(long id, UpdateDeviceRequest request);
    Task DeleteDeviceAsync(long id);
    Task<List<DeviceDto>> GetDevicesByAreaAsync(long areaId, string? appCode, List<long>? allowedAreaIds);
    Task<DeviceDetailDto?> GetDeviceDetailAsync(long id, string? appCode, List<long>? allowedAreaIds);
}
