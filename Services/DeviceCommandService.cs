using System.Security.Claims;
using IoTPlatform.Data;
using IoTPlatform.DTOs.Requests;
using IoTPlatform.DTOs.Responses;
using IoTPlatform.Helpers;
using IoTPlatform.Models;
using IoTPlatform.Services.Guards;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace IoTPlatform.Services;

/// <summary>
/// 设备指令服务实现
/// 负责指令下发、状态跟踪、历史查询及 MQTT 集成
/// </summary>
public class DeviceCommandService : IDeviceCommandService
{
    private readonly AppDbContext _db;
    private readonly IMqttClientService _mqttClient;
    private readonly IAnShengCommandService? _anShengCommandService;
    private readonly ILogger<DeviceCommandService> _logger;
    private readonly IDeviceAccessGuard? _deviceGuard;
    private readonly ICustomerScopeService? _scope;
    private readonly ICustomerScopeGuard? _scopeGuard;

    public DeviceCommandService(
        AppDbContext db,
        IMqttClientService mqttClient,
        ILogger<DeviceCommandService> logger,
        IAnShengCommandService? anShengCommandService = null,
        IDeviceAccessGuard? deviceGuard = null,
        ICustomerScopeService? scope = null,
        ICustomerScopeGuard? scopeGuard = null)
    {
        _db = db;
        _mqttClient = mqttClient;
        _anShengCommandService = anShengCommandService;
        _logger = logger;
        _deviceGuard = deviceGuard;
        _scope = scope;
        _scopeGuard = scopeGuard;

        // 监听 MQTT 指令响应事件
        _mqttClient.OnCommandResponse += OnCommandResponseReceived;
    }

    // ─────────────────────────────────────────────
    // 发送指令
    // ─────────────────────────────────────────────

    /// <inheritdoc />
    public Task<DeviceCommandResponse> SendCommandAsync(
        SendDeviceCommandRequest request,
        string? appCode,
        long? userId,
        string? userName)
        => SendCommandCoreAsync(request, appCode, userId, userName, principal: null, ct: default);

    /// <inheritdoc />
    public Task<DeviceCommandResponse> SendCommandAsync(
        SendDeviceCommandRequest request,
        string? appCode,
        long? userId,
        string? userName,
        ClaimsPrincipal principal,
        CancellationToken ct = default)
    {
        RequirePrincipal(principal);
        EnsureGuardsAvailable();
        return SendCommandCoreAsync(request, appCode, userId, userName, principal, ct);
    }

    /// <summary>
    /// 发送指令核心实现。principal 非空时先过设备归属守卫，越权在任何写库/MQTT 之前抛出（原则 G）。
    /// </summary>
    private async Task<DeviceCommandResponse> SendCommandCoreAsync(
        SendDeviceCommandRequest request,
        string? appCode,
        long? userId,
        string? userName,
        ClaimsPrincipal? principal,
        CancellationToken ct)
    {
        // 【B1/M4 双保险】Controller 已用 Guard 前置校验；Service 再校验一次，判据同源。
        if (principal != null)
        {
            await _deviceGuard!.EnsureCanOperateAsync(principal, request.DeviceId, ct).ConfigureAwait(false);
        }

        // 1. 验证设备存在性
        var device = await _db.Devices
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(d => d.Id == request.DeviceId &&
                                      (appCode == null || d.AppCode == appCode));

        if (device == null)
        {
            return new DeviceCommandResponse
            {
                Success = false,
                ErrorMessage = $"设备 {request.DeviceId} 不存在或无权限访问"
            };
        }

        // 归属（AppCode）以服务端点查的设备为准；admin 无单值 claim 时不再把 null 落库。
        appCode = string.IsNullOrWhiteSpace(appCode) ? device.AppCode : appCode;

        // ★ 命令路由：如果设备配置了安圣协议（ProtocolConfigId 不为空），
        //    则通过安圣适配器下发，而非通用 MQTT 客户端
        if (device.ProtocolConfigId != null && _anShengCommandService != null)
        {
            return await SendViaAnShengAsync(device, request, appCode, userId, userName, principal, ct).ConfigureAwait(false);
        }

        // 2. 序列化参数
        var parametersJson = request.Parameters != null
            ? JsonSerializer.Serialize(request.Parameters)
            : null;

        // 3. 创建指令记录
        var command = new DeviceCommand
        {
            CommandId = Guid.NewGuid().ToString("N"),
            AppCode = appCode,
            DeviceId = request.DeviceId,
            SerialNumber = device.SerialNumber,
            CommandType = request.CommandType,
            Parameters = parametersJson,
            Status = CommandStatus.Pending,
            TimeoutSeconds = request.TimeoutSeconds,
            MaxRetries = request.MaxRetries,
            CreatedBy = userId,
            CreatedByName = userName,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _db.DeviceCommands.Add(command);

        // 4. 记录创建历史
        _db.CommandHistories.Add(new CommandHistory
        {
            AppCode = appCode,
            CommandId = command.Id,
            Type = CommandHistoryType.Created,
            ToStatus = CommandStatus.Pending,
            Description = $"指令已创建，类型：{request.CommandType}",
            OperatorId = userId,
            OperatorName = userName,
            CreatedAt = DateTime.UtcNow
        });

        await _db.SaveChangesAsync();

        // 5. 通过 MQTT 下发指令
        try
        {
            await _mqttClient.SendDeviceCommandAsync(
                appCode ?? device.AppCode ?? "default",
                device.Id,
                command.CommandId,
                command.CommandType,
                command.Parameters);

            // 更新状态为已发送
            command.Status = CommandStatus.Sent;
            command.SentAt = DateTime.UtcNow;
            command.UpdatedAt = DateTime.UtcNow;

            _db.CommandHistories.Add(new CommandHistory
            {
                AppCode = appCode,
                CommandId = command.Id,
                Type = CommandHistoryType.Sent,
                FromStatus = CommandStatus.Pending,
                ToStatus = CommandStatus.Sent,
                Description = "指令已通过 MQTT 下发",
                OperatorId = userId,
                OperatorName = userName,
                CreatedAt = DateTime.UtcNow
            });

            await _db.SaveChangesAsync();

            _logger.LogInformation("指令下发成功: CommandId={CommandId}, DeviceId={DeviceId}, Type={Type}",
                command.CommandId, command.DeviceId, command.CommandType);
        }
        catch (Exception ex)
        {
            // MQTT 发送失败，标记为失败状态
            command.Status = CommandStatus.Failed;
            command.ErrorMessage = $"MQTT 下发失败：{ex.Message}";
            command.CompletedAt = DateTime.UtcNow;
            command.UpdatedAt = DateTime.UtcNow;

            _db.CommandHistories.Add(new CommandHistory
            {
                AppCode = appCode,
                CommandId = command.Id,
                Type = CommandHistoryType.Failed,
                FromStatus = CommandStatus.Pending,
                ToStatus = CommandStatus.Failed,
                Description = $"MQTT 下发失败：{ex.Message}",
                OperatorId = userId,
                OperatorName = userName,
                CreatedAt = DateTime.UtcNow
            });

            await _db.SaveChangesAsync();

            _logger.LogError(ex, "指令下发失败: CommandId={CommandId}, DeviceId={DeviceId}",
                command.CommandId, command.DeviceId);

            return new DeviceCommandResponse
            {
                Success = false,
                CommandId = command.CommandId,
                Status = command.Status.ToString(),
                ErrorMessage = command.ErrorMessage,
                CreatedAt = command.CreatedAt
            };
        }

        return new DeviceCommandResponse
        {
            Success = true,
            CommandId = command.CommandId,
            Status = command.Status.ToString(),
            CreatedAt = command.CreatedAt
        };
    }

    // ─────────────────────────────────────────────
    // 批量发送指令
    // ─────────────────────────────────────────────

    /// <inheritdoc />
    public Task<List<DeviceCommandResponse>> SendBatchCommandsAsync(
        List<SendDeviceCommandRequest> requests,
        string? appCode,
        long? userId,
        string? userName)
        => SendBatchCommandsCoreAsync(requests, appCode, userId, userName, principal: null, ct: default);

    /// <inheritdoc />
    public Task<List<DeviceCommandResponse>> SendBatchCommandsAsync(
        List<SendDeviceCommandRequest> requests,
        string? appCode,
        long? userId,
        string? userName,
        ClaimsPrincipal principal,
        CancellationToken ct = default)
    {
        RequirePrincipal(principal);
        EnsureGuardsAvailable();
        return SendBatchCommandsCoreAsync(requests, appCode, userId, userName, principal, ct);
    }

    /// <summary>批量下发核心实现：principal 透传给每一条单发调用。</summary>
    private async Task<List<DeviceCommandResponse>> SendBatchCommandsCoreAsync(
        List<SendDeviceCommandRequest> requests,
        string? appCode,
        long? userId,
        string? userName,
        ClaimsPrincipal? principal,
        CancellationToken ct)
    {
        var results = new List<DeviceCommandResponse>();
        foreach (var req in requests)
        {
            var result = principal == null
                ? await SendCommandCoreAsync(req, appCode, userId, userName, null, ct).ConfigureAwait(false)
                : await SendCommandCoreAsync(req, appCode, userId, userName, principal, ct).ConfigureAwait(false);
            results.Add(result);
        }

        return results;
    }

    // ─────────────────────────────────────────────
    // 查询指令
    // ─────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<DeviceCommandDto?> GetCommandAsync(string commandId, string? appCode)
    {
        var query = _db.DeviceCommands
            .Include(c => c.Device)
            .Where(c => c.CommandId == commandId);

        if (!string.IsNullOrEmpty(appCode))
            query = query.Where(c => c.AppCode == appCode);

        var command = await query.FirstOrDefaultAsync();
        return command == null ? null : MapToDto(command);
    }

    /// <inheritdoc />
        /// <summary>旧签名保留：委托到范围下推重载，下级/内部路径零回归。</summary>
    public Task<PagedResponse<DeviceCommandDto>> GetCommandsAsync(long deviceId, string? appCode, int page, int pageSize) => GetCommandsAsync(deviceId, appCode, page, pageSize, null, null);

    /// <summary>B4/M5：admin 的 appCode=null 不再等于零过滤。</summary>
    public async Task<PagedResponse<DeviceCommandDto>> GetCommandsAsync(
        long deviceId,
        string? appCode,
        int page,
        int pageSize, IReadOnlyCollection<long>? customerIds, IReadOnlyCollection<string>? appCodes = null)
    {
        var query = _db.DeviceCommands
            .Include(c => c.Device)
            .Where(c => c.DeviceId == deviceId);
        query = query.ApplyQueryScope(appCode, customerIds, appCodes);

        if (!string.IsNullOrEmpty(appCode))
            query = query.Where(c => c.AppCode == appCode);

        var total = await query.CountAsync();
        var items = await query
            .OrderByDescending(c => c.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return PagedResponse<DeviceCommandDto>.Create(
            items.Select(MapToDto).ToList(),
            total, page, pageSize);
    }

    // ─────────────────────────────────────────────
    // 指令历史
    // ─────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<List<CommandHistoryDto>> GetCommandHistoryAsync(string commandId, string? appCode)
    {
        // 先找到指令的数字 ID
        var commandQuery = _db.DeviceCommands.Where(c => c.CommandId == commandId);
        if (!string.IsNullOrEmpty(appCode))
            commandQuery = commandQuery.Where(c => c.AppCode == appCode);

        var command = await commandQuery.FirstOrDefaultAsync();
        if (command == null)
            return new List<CommandHistoryDto>();

        return await _db.CommandHistories
            .Where(h => h.CommandId == command.Id)
            .OrderBy(h => h.CreatedAt)
            .Select(h => new CommandHistoryDto
            {
                Id = h.Id,
                CommandId = h.CommandId,
                Type = h.Type.ToString(),
                FromStatus = h.FromStatus.HasValue ? h.FromStatus.Value.ToString() : null,
                ToStatus = h.ToStatus.HasValue ? h.ToStatus.Value.ToString() : null,
                Description = h.Description,
                Data = h.Data,
                OperatorId = h.OperatorId,
                OperatorName = h.OperatorName,
                CreatedAt = h.CreatedAt
            })
            .ToListAsync();
    }

    // ─────────────────────────────────────────────
    // 取消指令
    // ─────────────────────────────────────────────

    /// <inheritdoc />
    public Task<bool> CancelCommandAsync(string commandId, string? appCode)
        => CancelCommandCoreAsync(commandId, appCode, principal: null, ct: default);

    /// <inheritdoc />
    public Task<bool> CancelCommandAsync(
        string commandId, string? appCode, ClaimsPrincipal principal, CancellationToken ct = default)
    {
        RequirePrincipal(principal);
        EnsureGuardsAvailable();
        return CancelCommandCoreAsync(commandId, appCode, principal, ct);
    }

    /// <summary>
    /// 取消指令核心实现：先点查归属并断言范围，状态变更与 history 写入均在断言之后（原则 G）。
    /// </summary>
    private async Task<bool> CancelCommandCoreAsync(
        string commandId, string? appCode, ClaimsPrincipal? principal, CancellationToken ct)
    {
        var query = _db.DeviceCommands.IgnoreQueryFilters().Where(c => c.CommandId == commandId);
        if (!string.IsNullOrEmpty(appCode))
            query = query.Where(c => c.AppCode == appCode);

        var command = await query.FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (command == null)
            return false;

        if (principal != null)
        {
            await AssertCommandScopeAsync(command.AppCode, principal, ct).ConfigureAwait(false);
        }

        // 只有 Pending / Sent 状态可以取消
        if (command.Status != CommandStatus.Pending && command.Status != CommandStatus.Sent)
            return false;

        var oldStatus = command.Status;
        command.Status = CommandStatus.Cancelled;
        command.CompletedAt = DateTime.UtcNow;
        command.UpdatedAt = DateTime.UtcNow;

        _db.CommandHistories.Add(new CommandHistory
        {
            AppCode = command.AppCode,
            CommandId = command.Id,
            Type = CommandHistoryType.Cancelled,
            FromStatus = oldStatus,
            ToStatus = CommandStatus.Cancelled,
            Description = "指令已被用户取消",
            CreatedAt = DateTime.UtcNow
        });

        await _db.SaveChangesAsync();
        return true;
    }

    // ─────────────────────────────────────────────
    // 状态更新（MQTT 回调）
    // ─────────────────────────────────────────────

    /// <inheritdoc />
    public async Task UpdateCommandStatusAsync(
        string commandId,
        CommandStatus status,
        string? result = null,
        string? errorMessage = null)
    {
        var command = await _db.DeviceCommands
            .FirstOrDefaultAsync(c => c.CommandId == commandId);

        if (command == null)
        {
            _logger.LogWarning("UpdateCommandStatus: 找不到指令 CommandId={CommandId}", commandId);
            return;
        }

        var oldStatus = command.Status;
        command.Status = status;
        command.Result = result;
        command.ErrorMessage = errorMessage;
        command.UpdatedAt = DateTime.UtcNow;

        if (status == CommandStatus.Success ||
            status == CommandStatus.Failed ||
            status == CommandStatus.Timeout ||
            status == CommandStatus.Cancelled)
        {
            command.CompletedAt = DateTime.UtcNow;
        }

        var historyType = status switch
        {
            CommandStatus.Delivered => CommandHistoryType.Received,
            CommandStatus.Success => CommandHistoryType.Success,
            CommandStatus.Failed => CommandHistoryType.Failed,
            CommandStatus.Timeout => CommandHistoryType.Timeout,
            _ => CommandHistoryType.Response
        };

        _db.CommandHistories.Add(new CommandHistory
        {
            AppCode = command.AppCode,
            CommandId = command.Id,
            Type = historyType,
            FromStatus = oldStatus,
            ToStatus = status,
            Description = errorMessage ?? result ?? $"状态变更为 {status}",
            Data = result,
            CreatedAt = DateTime.UtcNow
        });

        await _db.SaveChangesAsync();

        _logger.LogInformation("指令状态已更新: CommandId={CommandId}, {Old} -> {New}",
            commandId, oldStatus, status);
    }

    // ─────────────────────────────────────────────
    // 重试指令
    // ─────────────────────────────────────────────

    /// <inheritdoc />
    public Task<bool> RetryCommandAsync(string commandId)
        => RetryCommandCoreAsync(commandId, principal: null, ct: default);

    /// <inheritdoc />
    public Task<bool> RetryCommandAsync(
        string commandId, ClaimsPrincipal principal, CancellationToken ct = default)
    {
        RequirePrincipal(principal);
        EnsureGuardsAvailable();
        return RetryCommandCoreAsync(commandId, principal, ct);
    }

    /// <summary>
    /// 重试指令核心实现：归属断言在 RetryCount++/SaveChanges/MQTT 重新下发之前（原则 G）。
    /// </summary>
    private async Task<bool> RetryCommandCoreAsync(
        string commandId, ClaimsPrincipal? principal, CancellationToken ct)
    {
        var command = await _db.DeviceCommands
            .IgnoreQueryFilters()
            .Include(c => c.Device)
            .FirstOrDefaultAsync(c => c.CommandId == commandId, ct)
            .ConfigureAwait(false);

        if (command == null)
            return false;

        if (principal != null)
        {
            await AssertCommandScopeAsync(command.AppCode, principal, ct).ConfigureAwait(false);
        }

        // 只允许重试失败或超时的指令
        if (command.Status != CommandStatus.Failed && command.Status != CommandStatus.Timeout)
            return false;

        // 检查重试次数
        if (command.RetryCount >= command.MaxRetries)
        {
            _logger.LogWarning("指令已超过最大重试次数: CommandId={CommandId}, RetryCount={Count}",
                commandId, command.RetryCount);
            return false;
        }

        command.RetryCount++;
        command.Status = CommandStatus.Pending;
        command.ErrorMessage = null;
        command.UpdatedAt = DateTime.UtcNow;

        _db.CommandHistories.Add(new CommandHistory
        {
            AppCode = command.AppCode,
            CommandId = command.Id,
            Type = CommandHistoryType.Retry,
            FromStatus = CommandStatus.Failed,
            ToStatus = CommandStatus.Pending,
            Description = $"第 {command.RetryCount} 次重试",
            CreatedAt = DateTime.UtcNow
        });

        await _db.SaveChangesAsync();

        // 重新下发
        try
        {
            await _mqttClient.SendDeviceCommandAsync(
                command.AppCode ?? "default",
                command.DeviceId,
                command.CommandId,
                command.CommandType,
                command.Parameters);

            command.Status = CommandStatus.Sent;
            command.SentAt = DateTime.UtcNow;
            command.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            command.Status = CommandStatus.Failed;
            command.ErrorMessage = $"重试下发失败：{ex.Message}";
            command.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            _logger.LogError(ex, "指令重试下发失败: CommandId={CommandId}", commandId);
            return false;
        }

        return true;
    }

    // ─────────────────────────────────────────────
    // 获取待处理指令
    // ─────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<List<DeviceCommand>> GetPendingCommandsAsync()
    {
        return await _db.DeviceCommands
            .Where(c => c.Status == CommandStatus.Pending || c.Status == CommandStatus.Sent)
            .OrderBy(c => c.CreatedAt)
            .ToListAsync();
    }

    // ─────────────────────────────────────────────
    // MQTT 指令响应回调
    // ─────────────────────────────────────────────

    private void OnCommandResponseReceived(object? sender, CommandResponseEventArgs e)
    {
        // 异步处理，避免阻塞 MQTT 线程
        _ = Task.Run(async () =>
        {
            try
            {
                var status = e.Status.ToLowerInvariant() switch
                {
                    "success" => CommandStatus.Success,
                    "delivered" => CommandStatus.Delivered,
                    "failed" => CommandStatus.Failed,
                    "timeout" => CommandStatus.Timeout,
                    _ => CommandStatus.Failed
                };

                await UpdateCommandStatusAsync(e.CommandId, status, e.ResultData, e.ErrorMessage);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "处理指令响应时发生错误: CommandId={CommandId}", e.CommandId);
            }
        });
    }

    // ─────────────────────────────────────────────
    // 安圣协议命令路由
    // ─────────────────────────────────────────────

    /// <summary>
    /// 通过安圣适配器下发命令
    /// 当设备配置了 ProtocolConfigId 时走此路径
    /// </summary>
    private async Task<DeviceCommandResponse> SendViaAnShengAsync(
        Models.Device device,
        SendDeviceCommandRequest request,
        string? appCode,
        long? userId,
        string? userName,
        ClaimsPrincipal? principal,
        CancellationToken ct)
    {
        // 创建指令记录（状态先 Pending）
        var parametersJson = request.Parameters != null
            ? System.Text.Json.JsonSerializer.Serialize(request.Parameters)
            : null;

        var command = new DeviceCommand
        {
            CommandId = Guid.NewGuid().ToString("N"),
            AppCode = appCode,
            DeviceId = request.DeviceId,
            SerialNumber = device.SerialNumber,
            CommandType = request.CommandType,
            Parameters = parametersJson,
            Status = CommandStatus.Pending,
            TimeoutSeconds = request.TimeoutSeconds,
            MaxRetries = request.MaxRetries,
            CreatedBy = userId,
            CreatedByName = userName,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _db.DeviceCommands.Add(command);

        _db.CommandHistories.Add(new CommandHistory
        {
            AppCode = appCode,
            CommandId = command.Id,
            Type = CommandHistoryType.Created,
            ToStatus = CommandStatus.Pending,
            Description = $"指令已创建（安圣协议），类型：{request.CommandType}",
            OperatorId = userId,
            OperatorName = userName,
            CreatedAt = DateTime.UtcNow
        });

        await _db.SaveChangesAsync();

        // 通过安圣服务下发
        try
        {
            // T7-3：透传 CommandId —— DeviceCommand.CommandId 即 AnShengCommandRecord.CommandId，
            // 二者同值构成「单一真相」，取代原 R1 静态 frameId→commandId 映射（已物理删除）。
            // 【B1/M4 遗留裸通道收口】外部命令必须显式传 principal，由 AnShengCommandService
            // 单点 Guard 在 MQTT publish 之前校验归属；内部通道仍走旧签名，行为逐字不变。
            var result = principal == null
                ? await _anShengCommandService!.SendCommandAsync(
                    deviceId: request.DeviceId,
                    method: request.CommandType,
                    parameters: request.Parameters,
                    ct: ct == default ? CancellationToken.None : ct,
                    commandId: command.CommandId)
                : await _anShengCommandService!.SendCommandAsync(
                    deviceId: request.DeviceId,
                    method: request.CommandType,
                    parameters: request.Parameters,
                    principal: principal,
                    ct: ct,
                    commandId: command.CommandId);

            if (result.Success)
            {
                command.Status = CommandStatus.Sent;
                command.SentAt = DateTime.UtcNow;
                command.UpdatedAt = DateTime.UtcNow;

                _db.CommandHistories.Add(new CommandHistory
                {
                    AppCode = appCode,
                    CommandId = command.Id,
                    Type = CommandHistoryType.Sent,
                    FromStatus = CommandStatus.Pending,
                    ToStatus = CommandStatus.Sent,
                    Description = $"安圣命令已下发: FrameId={result.FrameId}",
                    OperatorId = userId,
                    OperatorName = userName,
                    CreatedAt = DateTime.UtcNow
                });

                await _db.SaveChangesAsync();

                _logger.LogInformation(
                    "安圣命令下发成功: CommandId={CommandId}, DeviceId={DeviceId}, Method={Method}, FrameId={FrameId}",
                    command.CommandId, command.DeviceId, command.CommandType, result.FrameId);
            }
            else
            {
                command.Status = CommandStatus.Failed;
                command.ErrorMessage = result.ErrorMessage;
                command.CompletedAt = DateTime.UtcNow;
                command.UpdatedAt = DateTime.UtcNow;

                _db.CommandHistories.Add(new CommandHistory
                {
                    AppCode = appCode,
                    CommandId = command.Id,
                    Type = CommandHistoryType.Failed,
                    FromStatus = CommandStatus.Pending,
                    ToStatus = CommandStatus.Failed,
                    Description = $"安圣命令下发失败：{result.ErrorMessage}",
                    OperatorId = userId,
                    OperatorName = userName,
                    CreatedAt = DateTime.UtcNow
                });

                await _db.SaveChangesAsync();

                _logger.LogWarning(
                    "安圣命令下发失败: CommandId={CommandId}, Error={Error}",
                    command.CommandId, result.ErrorMessage);

                return new DeviceCommandResponse
                {
                    Success = false,
                    CommandId = command.CommandId,
                    Status = command.Status.ToString(),
                    ErrorMessage = command.ErrorMessage,
                    CreatedAt = command.CreatedAt
                };
            }
        }
        catch (Exception ex)
        {
            command.Status = CommandStatus.Failed;
            command.ErrorMessage = $"安圣命令下发异常：{ex.Message}";
            command.CompletedAt = DateTime.UtcNow;
            command.UpdatedAt = DateTime.UtcNow;

            _db.CommandHistories.Add(new CommandHistory
            {
                AppCode = appCode,
                CommandId = command.Id,
                Type = CommandHistoryType.Failed,
                FromStatus = CommandStatus.Pending,
                ToStatus = CommandStatus.Failed,
                Description = $"安圣命令下发异常：{ex.Message}",
                OperatorId = userId,
                OperatorName = userName,
                CreatedAt = DateTime.UtcNow
            });

            await _db.SaveChangesAsync();

            _logger.LogError(ex,
                "安圣命令下发异常: CommandId={CommandId}, DeviceId={DeviceId}",
                command.CommandId, command.DeviceId);

            return new DeviceCommandResponse
            {
                Success = false,
                CommandId = command.CommandId,
                Status = command.Status.ToString(),
                ErrorMessage = command.ErrorMessage,
                CreatedAt = command.CreatedAt
            };
        }

        return new DeviceCommandResponse
        {
            Success = true,
            CommandId = command.CommandId,
            Status = command.Status.ToString(),
            CreatedAt = command.CreatedAt
        };
    }

    // ─────────────────────────────────────────────
    // 归属断言（B1/M4 双保险）
    // ─────────────────────────────────────────────

    /// <summary>外部调用必须带 principal；禁止把 null 当作“跳过校验”。</summary>
    private static void RequirePrincipal(ClaimsPrincipal principal)
    {
        if (principal == null)
        {
            throw new UnauthorizedAccessException("外部命令通道必须显式传递调用方身份");
        }
    }

    /// <summary> Guard 依赖缺失时 fail-closed，不允许静默降级为裸通道。</summary>
    private void EnsureGuardsAvailable()
    {
        if (_deviceGuard == null || _scope == null || _scopeGuard == null)
        {
            throw new InvalidOperationException("DeviceCommandService 缺少归属守卫依赖，禁止处理外部命令");
        }
    }

    /// <summary>
    /// 指令 AppCode → CustomerId → CustomerScopeGuard.AssertScope（与 Controller 同一判据）。
    /// AppCode 缺失或无法反查归属时，AssertScope 对非超管抛 ForbiddenAccessException。
    /// </summary>
    private async Task AssertCommandScopeAsync(string? appCode, ClaimsPrincipal principal, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(appCode))
        {
            var scope = await _scope!.ResolveAsync(principal, ct).ConfigureAwait(false);
            _scopeGuard!.AssertScope(scope, null, appCode);
            return;
        }

        var customerId = await _scope!.ResolveCustomerIdByAppCodeAsync(appCode, ct).ConfigureAwait(false);
        var currentScope = await _scope.ResolveAsync(principal, ct).ConfigureAwait(false);
        _scopeGuard!.AssertScope(currentScope, customerId, appCode);
    }

    // ─────────────────────────────────────────────
    // 映射辅助
    // ─────────────────────────────────────────────

    private static DeviceCommandDto MapToDto(DeviceCommand c)
    {
        Dictionary<string, object>? parameters = null;
        if (!string.IsNullOrEmpty(c.Parameters))
        {
            try
            {
                parameters = JsonSerializer.Deserialize<Dictionary<string, object>>(c.Parameters);
            }
            catch { /* 忽略反序列化错误 */ }
        }

        return new DeviceCommandDto
        {
            Id = c.Id,
            CommandId = c.CommandId,
            AppCode = c.AppCode,
            DeviceId = c.DeviceId,
            SerialNumber = c.SerialNumber,
            DeviceName = c.Device?.Name,
            CommandType = c.CommandType,
            Parameters = parameters,
            Status = c.Status.ToString(),
            Result = c.Result,
            ErrorMessage = c.ErrorMessage,
            SentAt = c.SentAt,
            CompletedAt = c.CompletedAt,
            RetryCount = c.RetryCount,
            CreatedAt = c.CreatedAt,
            UpdatedAt = c.UpdatedAt,
            CreatedBy = c.CreatedBy,
            CreatedByName = c.CreatedByName
        };
    }
}
