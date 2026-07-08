using IoTPlatform.Data.Repositories.Interfaces;
using IoTPlatform.DTOs.Requests;
using IoTPlatform.DTOs.Responses;
using IoTPlatform.Helpers;
using IoTPlatform.Infrastructure.Protocol;
using IoTPlatform.Infrastructure.Protocol.Adapters;
using IoTPlatform.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace IoTPlatform.Services;

/// <summary>
/// 协议配置服务实现（使用仓储模式）
///
/// 生命周期管理 + 协议适配器事件桥接：
/// StartProtocolAsync 中创建适配器并订阅 DataReceived 事件，
/// 将协议采集数据桥接到 IDataCollectionService.ProcessDeviceDataAsync()。
/// StopProtocolAsync 中取消订阅后释放适配器。
/// </summary>
public class ProtocolConfigService : IProtocolConfigService
{
    private readonly IProtocolConfigRepository _protocolConfigRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IProtocolAdapterFactory _adapterFactory;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IAnShengDiscoveryService? _discoveryService;
    private readonly ILogger<ProtocolConfigService>? _logger;

    /// <summary>
    /// 活跃的事件订阅字典：configId → 事件处理器（用于停止时反注册）
    /// </summary>
    private readonly Dictionary<int, EventHandler<DeviceDataReceivedEventArgs>> _activeSubscriptions = new();

    public ProtocolConfigService(
        IProtocolConfigRepository protocolConfigRepository,
        IUnitOfWork unitOfWork,
        IProtocolAdapterFactory adapterFactory,
        IServiceScopeFactory scopeFactory,
        IAnShengDiscoveryService? discoveryService = null,
        ILogger<ProtocolConfigService>? logger = null)
    {
        _protocolConfigRepository = protocolConfigRepository;
        _unitOfWork = unitOfWork;
        _adapterFactory = adapterFactory;
        _scopeFactory = scopeFactory;
        _discoveryService = discoveryService;
        _logger = logger;
    }

    /// <summary>
    /// 获取协议配置列表
    /// </summary>
    public async Task<PagedResponse<ProtocolConfigDto>> GetProtocolConfigsAsync(int page, int pageSize, string? keyword, string? type, string? appCode)
    {
        var query = _protocolConfigRepository.GetQueryable();

        // 租户数据隔离
        if (!string.IsNullOrEmpty(appCode))
        {
            query = query.Where(p => p.AppCode == appCode);
        }

        // 类型筛选
        if (!string.IsNullOrEmpty(type))
        {
            query = query.Where(p => p.Type == type);
        }

        // 关键词搜索
        if (!string.IsNullOrEmpty(keyword))
        {
            query = query.Where(p =>
                p.Name.Contains(keyword) ||
                (p.Description != null && p.Description.Contains(keyword)));
        }

        var totalCount = await query.CountAsync();

        var configs = await query
            .OrderByDescending(p => p.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(p => new ProtocolConfigDto
            {
                Id = p.Id,
                Name = p.Name,
                Type = p.Type,
                Status = p.Status,
                DeviceIdsJson = p.DeviceIds,
                ConfigJson = p.Config,
                Description = p.Description,
                AppCode = p.AppCode,
                IsActive = p.IsActive,
                CreatedAt = p.CreatedAt,
                UpdatedAt = p.UpdatedAt
            })
            .ToListAsync();

        // 手动解析JSON
        foreach (var config in configs)
        {
            if (!string.IsNullOrEmpty(config.DeviceIdsJson))
            {
                config.DeviceIds = JsonSerializer.Deserialize<List<long>>(config.DeviceIdsJson, new JsonSerializerOptions());
            }
            if (!string.IsNullOrEmpty(config.ConfigJson))
            {
                config.Config = JsonSerializer.Deserialize<Dictionary<string, object>>(config.ConfigJson, new JsonSerializerOptions());
            }
        }

        return PagedResponse<ProtocolConfigDto>.Create(configs, totalCount, page, pageSize);
    }

    /// <summary>
    /// 获取协议配置详情
    /// </summary>
    public async Task<ProtocolConfigDto?> GetProtocolConfigAsync(long id, string? appCode)
    {
        var query = _protocolConfigRepository.GetQueryable();

        // 租户数据隔离
        if (!string.IsNullOrEmpty(appCode))
        {
            query = query.Where(p => p.AppCode == appCode);
        }

        var config = await query.FirstOrDefaultAsync(p => p.Id == id);
        if (config == null) return null;

        return new ProtocolConfigDto
        {
            Id = config.Id,
            Name = config.Name,
            Type = config.Type,
            Status = config.Status,
            DeviceIds = !string.IsNullOrEmpty(config.DeviceIds)
                ? JsonSerializer.Deserialize<List<long>>(config.DeviceIds)
                : null,
            Config = !string.IsNullOrEmpty(config.Config)
                ? JsonSerializer.Deserialize<Dictionary<string, object>>(config.Config)
                : null,
            Description = config.Description,
            AppCode = config.AppCode,
            IsActive = config.IsActive,
            CreatedAt = config.CreatedAt,
            UpdatedAt = config.UpdatedAt
        };
    }

    /// <summary>
    /// 创建协议配置
    /// </summary>
    public async Task<ProtocolConfigDto> CreateProtocolConfigAsync(CreateProtocolConfigRequest request)
    {
        var config = new ProtocolConfig
        {
            Name = request.Name,
            Type = request.Type,
            Status = "inactive",
            Description = request.Description,
            AppCode = request.AppCode,
            DeviceIds = request.DeviceIds != null
                ? JsonSerializer.Serialize(request.DeviceIds)
                : null,
            Config = request.Config != null
                ? JsonSerializer.Serialize(request.Config)
                : null,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _protocolConfigRepository.AddAsync(config);
        await _unitOfWork.SaveChangesAsync();

        return new ProtocolConfigDto
        {
            Id = config.Id,
            Name = config.Name,
            Type = config.Type,
            Status = config.Status,
            DeviceIds = request.DeviceIds,
            Config = request.Config,
            Description = config.Description,
            AppCode = config.AppCode,
            CreatedAt = config.CreatedAt,
            UpdatedAt = config.UpdatedAt
        };
    }

    /// <summary>
    /// 更新协议配置
    /// </summary>
    public async Task<ProtocolConfigDto> UpdateProtocolConfigAsync(long id, UpdateProtocolConfigRequest request, string? appCode)
    {
        var config = await _protocolConfigRepository.GetByIdAsync(id);
        if (config == null)
        {
            throw new InvalidOperationException("协议配置不存在");
        }

        // 权限检查
        if (!string.IsNullOrEmpty(appCode) && config.AppCode != appCode)
        {
            throw new UnauthorizedAccessException("无权修改该协议配置");
        }

        config.Name = request.Name;
        config.Status = request.Status;
        config.Description = request.Description ?? config.Description;
        config.AppCode = config.AppCode;
        config.IsActive = request.IsActive;
        config.DeviceIds = request.DeviceIds != null
            ? JsonSerializer.Serialize(request.DeviceIds)
            : config.DeviceIds;
        config.Config = request.Config != null
            ? JsonSerializer.Serialize(request.Config)
            : config.Config;
        config.UpdatedAt = DateTime.UtcNow;

        await _protocolConfigRepository.UpdateAsync(config);
        await _unitOfWork.SaveChangesAsync();

        return new ProtocolConfigDto
        {
            Id = config.Id,
            Name = config.Name,
            Type = config.Type,
            Status = config.Status,
            DeviceIds = request.DeviceIds,
            Config = request.Config,
            Description = config.Description,
            AppCode = config.AppCode,
            CreatedAt = config.CreatedAt,
            UpdatedAt = config.UpdatedAt
        };
    }

    /// <summary>
    /// 删除协议配置
    /// </summary>
    public async Task DeleteProtocolConfigAsync(long id, string? appCode)
    {
        var config = await _protocolConfigRepository.GetByIdAsync(id);
        if (config == null)
        {
            throw new InvalidOperationException("协议配置不存在");
        }

        // 权限检查
        if (!string.IsNullOrEmpty(appCode) && config.AppCode != appCode)
        {
            throw new UnauthorizedAccessException("无权删除该协议配置");
        }

        // 检查是否有活跃状态
        if (config.Status == "active")
        {
            throw new InvalidOperationException("协议处于活跃状态，无法删除。请先停止协议。");
        }

        await _protocolConfigRepository.DeleteAsync(config);
        await _unitOfWork.SaveChangesAsync();
    }

    /// <summary>
    /// 启动协议
    ///
    /// 执行流程：
    /// 1. 创建协议适配器（工厂缓存）
    /// 2. 连接并启动数据采集
    /// 3. 【P2】订阅 DataReceived 事件 → 桥接到 IDataCollectionService
    /// 4. 更新数据库状态为 active
    /// </summary>
    public async Task StartProtocolAsync(long id, string? appCode)
    {
        var config = await _protocolConfigRepository.GetByIdAsync(id);
        if (config == null)
        {
            throw new InvalidOperationException("协议配置不存在");
        }

        // 权限检查
        if (!string.IsNullOrEmpty(appCode) && config.AppCode != appCode)
        {
            throw new UnauthorizedAccessException("无权启动该协议配置");
        }

        // 如果已经激活，直接返回
        if (config.Status == "active")
        {
            return;
        }

        try
        {
            // 创建协议适配器
            var protocolType = config.Type.ToUpperInvariant();
            var adapter = _adapterFactory.CreateAdapter(protocolType, (int)config.Id);

            // 获取连接配置
            var connectionString = config.Config ?? "{}";

            // 添加 AppCode 到配置
            if (!string.IsNullOrEmpty(config.AppCode) && !connectionString.Contains("AppCode"))
            {
                var configDict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(connectionString) ?? new();
                configDict["AppCode"] = JsonSerializer.SerializeToElement(config.AppCode);
                connectionString = JsonSerializer.Serialize(configDict);
            }

            // 连接并启动数据采集
            var connected = await adapter.ConnectAsync(connectionString);
            if (connected)
            {
                await adapter.StartDataCollectionAsync();
                _logger?.LogInformation("协议已启动: {Name}, Type={Type}", config.Name, config.Type);

                // ═════════════ P2: 订阅 DataReceived 事件，桥接到主采集链路 ═════════════
                SubscribeAdapterDataReceived(adapter, (int)config.Id, config.AppCode);
            }
            else
            {
                throw new InvalidOperationException($"协议连接失败: {config.Name}");
            }

            // 更新状态
            config.Status = "active";
            config.UpdatedAt = DateTime.UtcNow;
            await _protocolConfigRepository.UpdateAsync(config);
            await _unitOfWork.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "启动协议失败: {Name}", config.Name);
            throw;
        }
    }

    /// <summary>
    /// 停止协议
    ///
    /// 执行流程：
    /// 1. 【P2】取消订阅 DataReceived 事件
    /// 2. 释放协议适配器（断开 + Dispose）
    /// 3. 更新数据库状态为 inactive
    /// </summary>
    public async Task StopProtocolAsync(long id, string? appCode)
    {
        var config = await _protocolConfigRepository.GetByIdAsync(id);
        if (config == null)
        {
            throw new InvalidOperationException("协议配置不存在");
        }

        // 权限检查
        if (!string.IsNullOrEmpty(appCode) && config.AppCode != appCode)
        {
            throw new UnauthorizedAccessException("无权停止该协议配置");
        }

        // 如果已经停止，直接返回
        if (config.Status == "inactive")
        {
            return;
        }

        try
        {
            // ═════════════ P2: 反注册 DataReceived 事件订阅 ═════════════
            UnsubscribeAdapterDataReceived((int)config.Id);

            // 释放协议适配器
            _adapterFactory.ReleaseAdapter((int)config.Id);
            _logger?.LogInformation("协议已停止: {Name}, Type={Type}", config.Name, config.Type);

            // 更新状态
            config.Status = "inactive";
            config.UpdatedAt = DateTime.UtcNow;
            await _protocolConfigRepository.UpdateAsync(config);
            await _unitOfWork.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "停止协议失败: {Name}", config.Name);
            throw;
        }
    }

    #region ── P2: 协议适配器 DataReceived 事件桥接到主采集链路 ──

    /// <summary>
    /// 为协议适配器订阅 DataReceived 事件，将采集数据桥接到 IDataCollectionService
    ///
    /// 设计要点：
    /// - 使用 IServiceScopeFactory 创建 Scope 以获取 Scoped 生命周期的 DataCollectionService
    /// - 异步 fire-and-forget 模式，不阻塞适配器的数据接收线程
    /// - 异常隔离：单条数据处理失败不影响后续数据
    /// - 通过 _activeSubscriptions 字典追踪订阅，确保 Stop 时能正确反注册
    /// </summary>
    private void SubscribeAdapterDataReceived(IProtocolAdapter adapter, int configId, string? appCode)
    {
        // 如果已有订阅，先反注册（防止重复）
        if (_activeSubscriptions.ContainsKey(configId))
        {
            UnsubscribeAdapterDataReceived(configId);
        }

        EventHandler<DeviceDataReceivedEventArgs> handler = async (sender, e) =>
        {
            await OnProtocolAdapterDataReceived(e, appCode);
        };

        adapter.DataReceived += handler;
        _activeSubscriptions[configId] = handler;

        _logger?.LogInformation(
            "已订阅协议适配器数据事件: ConfigId={ConfigId}, ProtocolType={ProtocolType}",
            configId, adapter.ProtocolType);
    }

    /// <summary>
    /// 取消指定协议配置的事件订阅
    /// </summary>
    private void UnsubscribeAdapterDataReceived(int configId)
    {
        if (_activeSubscriptions.TryGetValue(configId, out var handler))
        {
            var adapter = _adapterFactory.GetAdapter(configId);
            if (adapter != null)
            {
                adapter.DataReceived -= handler;
            }
            _activeSubscriptions.Remove(configId);

            _logger?.LogInformation("已取消协议适配器数据事件订阅: ConfigId={ConfigId}", configId);
        }
    }

    /// <summary>
    /// 协议适配器 DataReceived 事件核心处理器
    ///
    /// 将来自 Modbus / OPC UA / MQTT(通过适配器) 的统一格式数据，
    /// 转发给 IDataCollectionService.ProcessDeviceDataAsync() 进入标准采集管道。
    ///
    /// 特殊处理：ANSHENG_MQTT 适配器的 DeviceId=0 时，按 SerialNumber(IMEI) 查询设备
    /// </summary>
    private async Task OnProtocolAdapterDataReceived(DeviceDataReceivedEventArgs e, string? fallbackAppCode)
    {
        try
        {
            // 使用 Scope 工厂创建作用域，以获取 Scoped 服务（DataCollectionService 是 Scoped）
            using var scope = _scopeFactory.CreateScope();
            var dataCollectionService = scope.ServiceProvider.GetRequiredService<IDataCollectionService>();

            var appCode = !string.IsNullOrEmpty(e.AppCode) ? e.AppCode : fallbackAppCode;
            var deviceId = e.DeviceId;

            // ── AnSheng MQTT 适配器特殊处理：DeviceId=0 时按 IMEI 查找设备 ──
            if (deviceId == 0 && e.ProtocolType == "ANSHENG_MQTT" && !string.IsNullOrEmpty(e.SerialNumber))
            {
                var deviceRepo = scope.ServiceProvider.GetRequiredService<IRepository<Device>>();
                var device = (await deviceRepo.GetAsync(
                    d => d.SerialNumber == e.SerialNumber,
                    appCode: appCode)).FirstOrDefault();

                if (device != null)
                {
                    deviceId = device.Id;
                    appCode = device.AppCode ?? appCode;
                    _logger?.LogDebug(
                        "安圣 IMEI 映射成功: IMEI={IMEI} -> DeviceId={DeviceId}, AppCode={AppCode}",
                        e.SerialNumber, deviceId, appCode);
                }
                else
                {
                    // 设备未注册 — 进入待认领池
                    if (_discoveryService != null && e.ProtocolType == "ANSHENG_MQTT")
                    {
                        // 尝试从标准化数据中提取 model / netType
                        string? model = null, netType = null;
                        if (!string.IsNullOrEmpty(e.Data))
                        {
                            try
                            {
                                using var doc = System.Text.Json.JsonDocument.Parse(e.Data);
                                var root = doc.RootElement;
                                if (root.TryGetProperty("model", out var m)) model = m.GetString();
                                if (root.TryGetProperty("netType", out var n)) netType = n.GetString();
                            }
                            catch { /* ignore parse errors */ }
                        }

                        await _discoveryService.OnDeviceOnlineAsync(
                            e.SerialNumber, model, netType, appCode);
                    }

                    _logger?.LogInformation(
                        "安圣设备 IMEI 未匹配到已注册设备: IMEI={IMEI}，已进入待认领池",
                        e.SerialNumber);
                    return;
                }
            }

            // ── 已注册设备：通知上线 ──
            if (_discoveryService != null && e.ProtocolType == "ANSHENG_MQTT" &&
                !string.IsNullOrEmpty(e.SerialNumber) && deviceId > 0)
            {
                // fire-and-forget 通知 DiscoveryService，不阻塞采集管道
                _ = _discoveryService.OnDeviceOnlineAsync(e.SerialNumber, null, null, appCode);
            }

            _logger?.LogDebug(
                "协议适配器数据桥接: DeviceId={DeviceId}, SerialNumber={Serial}, " +
                "ProtocolType={ProtoType}, AppCode={AppCode}, DataLength={Len}",
                deviceId, e.SerialNumber, e.ProtocolType, appCode,
                e.Data?.Length ?? 0);

            // 调用标准数据采集处理流程（复用 P0/P1 已完善的 JSON 解析 + 规则引擎链路）
            await dataCollectionService.ProcessDeviceDataAsync(
                deviceId: deviceId,
                appCode: appCode,
                sensorData: e.Data,
                timestamp: e.ReceivedAt);
        }
        catch (Exception ex)
        {
            // 异常隔离：单条数据处理失败仅记录日志，不影响适配器运行
            _logger?.LogError(ex,
                "协议适配器数据桥接处理失败（已隔离）: DeviceId={DeviceId}, ProtocolType={ProtoType}",
                e.DeviceId, e.ProtocolType);
        }
    }

    #endregion
}
