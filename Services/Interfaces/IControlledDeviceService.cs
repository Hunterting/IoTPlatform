using IoTPlatform.DTOs.Requests;
using IoTPlatform.DTOs.Responses;
using IoTPlatform.Helpers;
using IoTPlatform.Models;

namespace IoTPlatform.Services.Interfaces;

/// <summary>
/// 受控设备服务接口
/// </summary>
public interface IControlledDeviceService
{
    /// <summary>
    /// 注册设备到控制系统
    /// </summary>
    Task<ControlledDeviceDto> RegisterDeviceAsync(long deviceId, string? appCode, long? userId, string? userName);

    /// <summary>
    /// 批量注册设备
    /// </summary>
    Task<List<ControlledDeviceDto>> RegisterDevicesAsync(List<long> deviceIds, string? appCode, long? userId, string? userName);

    /// <summary>
    /// 取消注册设备
    /// </summary>
    Task<bool> UnregisterDeviceAsync(long id, string? appCode);

    /// <summary>
    /// 更新受控设备
    /// </summary>
    Task<ControlledDeviceDto?> UpdateDeviceAsync(long id, UpdateControlledDeviceRequest request, string? appCode);

    /// <summary>
    /// 获取受控设备详情
    /// </summary>
    Task<ControlledDeviceDto?> GetDeviceAsync(long id, string? appCode);

    /// <summary>
    /// 获取受控设备列表
    /// </summary>
    Task<PagedResponse<ControlledDeviceDto>> GetDevicesAsync(string? appCode, int page, int pageSize, bool? isEnabled, bool? isFavorite);

    /// <summary>
    /// 检查设备是否已注册
    /// </summary>
    Task<bool> IsDeviceRegisteredAsync(long deviceId, string? appCode);

    /// <summary>
    /// 切换收藏状态
    /// </summary>
    Task<bool> ToggleFavoriteAsync(long id, string? appCode);

    /// <summary>
    /// 记录指令发送
    /// </summary>
    Task RecordCommandSentAsync(long id);
}
