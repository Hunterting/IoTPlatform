using System.Security.Claims;
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
    /// 发送设备控制指令（内部/后台通道旧签名，行为保持不变）。
    /// </summary>
    /// <param name="request">指令请求</param>
    /// <param name="appCode">租户代码</param>
    /// <param name="userId">用户ID</param>
    /// <param name="userName">用户名</param>
    /// <returns>发送结果</returns>
    Task<DeviceCommandResponse> SendCommandAsync(SendDeviceCommandRequest request, string? appCode, long? userId, string? userName);

    /// <summary>
    /// 【B1/M4】带调用方身份的发送入口：Controller 必须调用本重载。
    /// 归属校验在任何写库/MQTT 下发/安圣命令下发之前完成（原则 G）。
    /// </summary>
    /// <param name="request">指令请求</param>
    /// <param name="appCode">租户代码（仅作记录用途，归属一律由服务端派生）</param>
    /// <param name="userId">用户ID</param>
    /// <param name="userName">用户名</param>
    /// <param name="principal">当前请求 principal，禁止为 null。</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>发送结果</returns>
    Task<DeviceCommandResponse> SendCommandAsync(
        SendDeviceCommandRequest request, string? appCode, long? userId, string? userName,
        ClaimsPrincipal principal, CancellationToken ct = default);

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
    /// <summary>旧签名保留：内部/下级路径零回归。</summary>
    Task<PagedResponse<DeviceCommandDto>> GetCommandsAsync(long deviceId, string? appCode, int page, int pageSize);

    /// <summary>B4/M5：Multi 授权客户集合范围下推。</summary>
    Task<PagedResponse<DeviceCommandDto>> GetCommandsAsync(long deviceId, string? appCode, int page, int pageSize, IReadOnlyCollection<long>? customerIds, IReadOnlyCollection<string>? appCodes = null) => GetCommandsAsync(deviceId, appCode, page, pageSize);

    /// <summary>
    /// 获取指令历史
    /// </summary>
    /// <param name="commandId">指令ID</param>
    /// <param name="appCode">租户代码</param>
    /// <returns>历史记录列表</returns>
    Task<List<CommandHistoryDto>> GetCommandHistoryAsync(string commandId, string? appCode);

    /// <summary>
    /// 取消指令（内部/后台通道旧签名）。
    /// </summary>
    /// <param name="commandId">指令ID</param>
    /// <param name="appCode">租户代码</param>
    /// <returns>是否成功</returns>
    Task<bool> CancelCommandAsync(string commandId, string? appCode);

    /// <summary>
    /// 【B1/M4】带 principal 的取消入口：Service 层二次校验指令归属，越权零副作用。
    /// </summary>
    /// <param name="commandId">指令ID</param>
    /// <param name="appCode">租户代码</param>
    /// <param name="principal">当前请求 principal，禁止为 null。</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>是否成功</returns>
    Task<bool> CancelCommandAsync(string commandId, string? appCode, ClaimsPrincipal principal, CancellationToken ct = default);

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
    /// 重试失败指令（内部/后台通道旧签名）。
    /// </summary>
    /// <param name="commandId">指令ID</param>
    /// <returns>是否成功</returns>
    Task<bool> RetryCommandAsync(string commandId);

    /// <summary>
    /// 【B1/M4】带 principal 的重试入口：Service 层二次校验指令归属，越权不重新下发。
    /// </summary>
    /// <param name="commandId">指令ID</param>
    /// <param name="principal">当前请求 principal，禁止为 null。</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>是否成功</returns>
    Task<bool> RetryCommandAsync(string commandId, ClaimsPrincipal principal, CancellationToken ct = default);

    /// <summary>
    /// 批量发送指令（内部/后台通道旧签名）。
    /// </summary>
    /// <param name="requests">指令请求列表</param>
    /// <param name="appCode">租户代码</param>
    /// <param name="userId">用户ID</param>
    /// <param name="userName">用户名</param>
    /// <returns>批量发送结果</returns>
    Task<List<DeviceCommandResponse>> SendBatchCommandsAsync(List<SendDeviceCommandRequest> requests, string? appCode, long? userId, string? userName);

    /// <summary>
    /// 【B1/M4】带 principal 的批量发送入口：Controller 先批量校验全部 deviceId，再逐条下发。
    /// </summary>
    /// <param name="requests">指令请求列表</param>
    /// <param name="appCode">租户代码</param>
    /// <param name="userId">用户ID</param>
    /// <param name="userName">用户名</param>
    /// <param name="principal">当前请求 principal，禁止为 null。</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>批量发送结果</returns>
    Task<List<DeviceCommandResponse>> SendBatchCommandsAsync(
        List<SendDeviceCommandRequest> requests, string? appCode, long? userId, string? userName,
        ClaimsPrincipal principal, CancellationToken ct = default);
}
