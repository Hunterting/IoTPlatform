using IoTPlatform.DTOs.Requests;
using IoTPlatform.DTOs.Responses;
using IoTPlatform.Helpers;
using IoTPlatform.Models;

namespace IoTPlatform.Services;

/// <summary>
/// 设备指令服务接口
/// </summary>
public interface IDeviceCommandService
{
    /// <summary>
    /// 发送设备控制指令
    /// </summary>
    /// <param name="request">指令请求</param>
    /// <param name="appCode">租户代码</param>
    /// <param name="userId">用户ID</param>
    /// <param name="userName">用户名</param>
    /// <returns>发送结果</returns>
    Task<DeviceCommandResponse> SendCommandAsync(SendDeviceCommandRequest request, string? appCode, long? userId, string? userName);

    /// <summary>
    /// 查询指令状态
    /// </summary>
    /// <param name="commandId">指令ID</param>
    /// <param name="appCode">租户代码</param>
    /// <returns>指令信息</returns>
    Task<DeviceCommandDto?> GetCommandAsync(string commandId, string? appCode);

    /// <summary>
    /// 获取设备指令列表
    /// </summary>
    /// <param name="deviceId">设备ID</param>
    /// <param name="appCode">租户代码</param>
    /// <param name="page">页码</param>
    /// <param name="pageSize">每页数量</param>
    /// <returns>分页指令列表</returns>
    Task<PagedResponse<DeviceCommandDto>> GetCommandsAsync(long deviceId, string? appCode, int page, int pageSize);

    /// <summary>
    /// 获取指令历史
    /// </summary>
    /// <param name="commandId">指令ID</param>
    /// <param name="appCode">租户代码</param>
    /// <returns>历史记录列表</returns>
    Task<List<CommandHistoryDto>> GetCommandHistoryAsync(string commandId, string? appCode);

    /// <summary>
    /// 取消指令
    /// </summary>
    /// <param name="commandId">指令ID</param>
    /// <param name="appCode">租户代码</param>
    /// <returns>是否成功</returns>
    Task<bool> CancelCommandAsync(string commandId, string? appCode);

    /// <summary>
    /// 更新指令状态（内部使用，由MQTT回调触发）
    /// </summary>
    /// <param name="commandId">指令ID</param>
    /// <param name="status">新状态</param>
    /// <param name="result">结果</param>
    /// <param name="errorMessage">错误信息</param>
    Task UpdateCommandStatusAsync(string commandId, CommandStatus status, string? result = null, string? errorMessage = null);

    /// <summary>
    /// 获取所有待处理的指令
    /// </summary>
    /// <returns>待处理指令列表</returns>
    Task<List<DeviceCommand>> GetPendingCommandsAsync();

    /// <summary>
    /// 重试失败指令
    /// </summary>
    /// <param name="commandId">指令ID</param>
    /// <returns>是否成功</returns>
    Task<bool> RetryCommandAsync(string commandId);

    /// <summary>
    /// 批量发送指令
    /// </summary>
    /// <param name="requests">指令请求列表</param>
    /// <param name="appCode">租户代码</param>
    /// <param name="userId">用户ID</param>
    /// <param name="userName">用户名</param>
    /// <returns>批量发送结果</returns>
    Task<List<DeviceCommandResponse>> SendBatchCommandsAsync(List<SendDeviceCommandRequest> requests, string? appCode, long? userId, string? userName);
}
