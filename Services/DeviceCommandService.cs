using IoTPlatform.Data;
using IoTPlatform.DTOs.Requests;
using IoTPlatform.DTOs.Responses;
using IoTPlatform.Helpers;
using IoTPlatform.Models;
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

    public DeviceCommandService(
        AppDbContext db,
        IMqttClientService mqttClient,
        ILogger<DeviceCommandService> logger,
        IAnShengCommandService? anShengCommandService = null)
    {
        _db = db;
        _mqttClient = mqttClient;
        _anShengCommandService = anShengCommandService;
        _logger = logger;

        // 监听 MQTT 指令响应事件
        _mqttClient.OnCommandResponse += OnCommandResponseReceived;
    }

    // ─────────────────────────────────────────────
    // 发送指令
    // ─────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<DeviceCommandResponse> SendCommandAsync(
        SendDeviceCommandRequest request,
        string? appCode,
        long? userId,
        string? userName)
    {
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

        // ★ 命令路由：如果设备配置了安圣协议（ProtocolConfigId 不为空），
        //    则通过安圣适配器下发，而非通用 MQTT 客户端
        if (device.ProtocolConfigId != null && _anShengCommandService != null)
        {
            return await SendViaAnShengAsync(device, request, appCode, userId, userName);
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
    public async Task<List<DeviceCommandResponse>> SendBatchCommandsAsync(
        List<SendDeviceCommandRequest> requests,
        string? appCode,
        long? userId,
        string? userName)
    {
        var results = new List<DeviceCommandResponse>();
        foreach (var req in requests)
        {
            var result = await SendCommandAsync(req, appCode, userId, userName);
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
    public async Task<PagedResponse<DeviceCommandDto>> GetCommandsAsync(
        long deviceId,
        string? appCode,
        int page,
        int pageSize)
    {
        var query = _db.DeviceCommands
            .Include(c => c.Device)
            .Where(c => c.DeviceId == deviceId);

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
    public async Task<bool> CancelCommandAsync(string commandId, string? appCode)
    {
        var query = _db.DeviceCommands.Where(c => c.CommandId == commandId);
        if (!string.IsNullOrEmpty(appCode))
            query = query.Where(c => c.AppCode == appCode);

        var command = await query.FirstOrDefaultAsync();
        if (command == null)
            return false;

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
    public async Task<bool> RetryCommandAsync(string commandId)
    {
        var command = await _db.DeviceCommands
            .Include(c => c.Device)
            .FirstOrDefaultAsync(c => c.CommandId == commandId);

        if (command == null)
            return false;

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
        string? userName)
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
            var result = await _anShengCommandService!.SendCommandAsync(
                deviceId: request.DeviceId,
                method: request.CommandType,
                parameters: request.Parameters,
                ct: CancellationToken.None);

            if (result.Success)
            {
                // 注册 frameId ↔ commandId 映射
                if (result.FrameId != null)
                {
                    AnShengCommandService.RegisterFrameIdMapping(result.FrameId, command.CommandId);
                }

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
