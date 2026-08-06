using IoTPlatform.Data.Repositories.Interfaces;
using IoTPlatform.DTOs.Requests;
using IoTPlatform.DTOs.Responses;
using IoTPlatform.Helpers;
using IoTPlatform.Infrastructure.Protocol;
using IoTPlatform.Infrastructure.Protocol.Adapters;
using IoTPlatform.Configuration;
using IoTPlatform.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
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
    private readonly AnShengEventOptions _eventOptions;

    /// <summary>
    /// 一条已建立的 <see cref="IProtocolAdapter.DataReceived"/> 订阅登记项。
    /// </summary>
    /// <remarks>
    /// 必须同时记住<b>适配器实例引用</b>而不只是 handler：反注册时不能依赖
    /// <see cref="IProtocolAdapterFactory.GetAdapter"/> 重新取一次 —— 适配器可能已被释放（返回 null）
    /// 或已被重建（返回的是另一个实例），那样 <c>-=</c> 就会作用在错误的对象上，
    /// 旧实例的 handler 永远摘不掉，成为「幽灵订阅」继续处理数据。
    /// </remarks>
    /// <param name="Adapter">建立订阅时所针对的适配器实例。</param>
    /// <param name="Handler">注册到 <c>DataReceived</c> 上的委托实例（<c>-=</c> 必须用同一个）。</param>
    private sealed record AdapterSubscription(
        IProtocolAdapter Adapter,
        EventHandler<DeviceDataReceivedEventArgs> Handler);

    /// <summary>
    /// 活跃的事件订阅登记表：configId → <see cref="AdapterSubscription"/>（用于去重与停止时反注册）。
    /// </summary>
    /// <remarks>
    /// 【为什么必须是 static —— 这是本次加固的核心修正】
    /// 本服务在 <c>Program.cs:120</c> 注册为 <b>Scoped</b>，而适配器由
    /// <c>Program.cs:47</c> 注册的 <b>Singleton</b> <see cref="ProtocolAdapterFactory"/> 持有，
    /// 存活于整个进程。原先该字段是实例字段，于是每个 HTTP 请求都拿到一张<b>空表</b>，
    /// <see cref="SubscribeAdapterDataReceived"/> 里「已有订阅先反注册」的防重判断<b>从来没有生效过</b>：
    /// 每调一次 <c>/start</c> 就往同一个单例适配器的 <c>DataReceived</c> 上再挂一个 handler，
    /// 同一份报文被处理 N 次（N 次入库 + N 次 <c>OnDeviceOnlineAsync</c>）。
    ///
    /// 这个缺陷此前被 <see cref="StartProtocolAsync"/> 的 <c>Status=="active"</c> 短路意外掩盖住了
    /// —— 第二次启动直接返回，走不到订阅那一行。本次放宽短路条件后该路径会被真实触发，
    /// 因此登记表的生命周期必须与它所追踪的对象（单例工厂里的适配器）对齐，即进程级。
    ///
    /// 【为什么不新增一个 Singleton 注册到 DI】
    /// 那需要改 <c>Program.cs</c> 与两处测试替身工厂的接线，超出本次最小变更范围；
    /// 而按 configId 做键的进程级字典，与 <c>ProtocolAdapterFactory._adapters</c> 的键完全同构，
    /// 语义上就是那张字典的伴生表。若后续要做启动自动恢复，建议连同本表一起提为独立单例组件。
    ///
    /// 【并发】控制面操作（启停协议）频次极低，用一把 <see cref="_subscriptionLock"/> 粗锁保证
    /// 「查—摘—挂—记」四步原子，比 ConcurrentDictionary 的多次 CAS 更易证明正确性。
    /// </remarks>
    private static readonly Dictionary<int, AdapterSubscription> _activeSubscriptions = new();

    /// <summary>
    /// 保护 <see cref="_activeSubscriptions"/> 的锁；订阅与反注册必须在同一把锁下完成。
    /// </summary>
    private static readonly object _subscriptionLock = new();

    public ProtocolConfigService(
        IProtocolConfigRepository protocolConfigRepository,
        IUnitOfWork unitOfWork,
        IProtocolAdapterFactory adapterFactory,
        IServiceScopeFactory scopeFactory,
        IAnShengDiscoveryService? discoveryService = null,
        ILogger<ProtocolConfigService>? logger = null,
        IOptions<AnShengEventOptions>? eventOptions = null)
    {
        _protocolConfigRepository = protocolConfigRepository;
        _unitOfWork = unitOfWork;
        _adapterFactory = adapterFactory;
        _scopeFactory = scopeFactory;
        _discoveryService = discoveryService;
        _logger = logger;
        _eventOptions = eventOptions?.Value ?? new AnShengEventOptions();
    }

    /// <summary>
    /// 是否抑制既有数据桥（<c>ProtocolConfigService → IDataCollectionService</c>）对事件报文的落库。
    ///
    /// 【决策 B-2 逃生开关，默认 false】事件报文会在「事件责任链」与「既有数据桥」两条通路上
    /// 各写一条 <c>DeviceDataRecord</c>。T6 默认<b>不抑制</b>（D4 §370 并存而非替换）。
    /// 仅当确认事件表数据可信、且希望消除重复落库时，由配置
    /// <c>AnSheng:Event:SuppressLegacyDataBridge=true</c> 打开。
    ///
    /// ⚠️ 当前为<b>钩子</b>：本服务无法在桥内区分「事件报文」与「自动上报报文」，
    /// 因此真正生效的去重逻辑留待后续接入（届时需由上行总线携带事件标记透传到本桥）。
    /// 开启此开关会同时影响自动上报链路，启用前请评估影响面。
    /// </summary>
    public bool ShouldSkipLegacyBridge => _eventOptions.SuppressLegacyDataBridge;

    #region ── 协议标识 / 生命周期字段派生 ──

    /// <summary>
    /// 从 <see cref="ProtocolConfig.Type"/> 派生 <see cref="ProtocolConfig.ProtocolType"/>。
    /// </summary>
    /// <remarks>
    /// 【为什么要在服务层派生，而不是让前端多传一个字段】
    /// <c>ProtocolType</c> 是消费侧唯一的协议筛选字段（安圣发现扫描、认领时解析适配器、命令下发共三处），
    /// 但它既不在 <see cref="CreateProtocolConfigRequest"/> 中，前端也从不提交，
    /// 于是经 API 建的配置该列恒为 NULL，等值筛选永远为空 —— 安圣设备无法被发现、认领、下发，
    /// 只能手工改库绕过。从已有的 <c>Type</c> 派生可在不改动前端契约的前提下闭合这条链路。
    ///
    /// 【归一规则为什么只做 ToUpperInvariant】
    /// <see cref="StartProtocolAsync"/> 传给 <see cref="IProtocolAdapterFactory.CreateAdapter"/> 的正是
    /// <c>config.Type.ToUpperInvariant()</c>，工厂内部再按同样规则匹配 switch 分支。
    /// 这里沿用同一条规则，保证「能创建出适配器的配置」与「消费侧能筛到的配置」严格是同一批：
    /// <c>ansheng_mqtt</c> → <c>ANSHENG_MQTT</c>，与 <c>AnShengDiscoveryService.AnShengProtocolType</c> 逐字相等。
    /// 刻意<b>不做</b> Trim 或分隔符归一 —— 一旦这里比工厂更宽容，就会造出
    /// 「ProtocolType 筛得到、适配器却建不出来」的假活跃配置，比当前缺陷更难定位。
    ///
    /// 【为什么非安圣协议也一并派生】
    /// 代码实证：全仓 <c>ProtocolConfig.ProtocolType</c> 的运行期消费方只有三处，且都是
    /// <c>== "ANSHENG_MQTT"</c> 的等值筛选，无任何一处读取非安圣取值，也没有判空（<c>== null</c>）筛选。
    /// 故统一派生对存量 mqtt / modbus / opcua / http / tcp / bacnet 的行为零影响；
    /// 同时与集成测试 <c>SeedData</c>「Type 与 ProtocolType 同值」的既有约定、
    /// 以及 <c>docs/system_design.md</c>「ProtocolType 由后端自动设置」一致。
    /// 只给安圣族派生反而会留下一个需要长期解释、且新增协议族时必然再次踩到的特例。
    /// </remarks>
    /// <param name="type">协议类型原始值（如 <c>ansheng_mqtt</c> / <c>mqtt</c> / <c>modbus</c>）。</param>
    /// <returns>归一化后的协议标识；输入为空或纯空白时返回 <c>null</c>，避免写入无意义的空串。</returns>
    public static string? DeriveProtocolType(string? type)
        => string.IsNullOrWhiteSpace(type) ? null : type.ToUpperInvariant();

    /// <summary>
    /// 判定生命周期状态字面量是否表示「运行中」。
    /// </summary>
    /// <param name="status">状态字面量（<c>active</c> / <c>inactive</c>）。</param>
    /// <returns>表示运行中返回 <c>true</c>。</returns>
    private static bool IsRunningStatus(string? status)
        => string.Equals(status, "active", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 幂等地把 <see cref="ProtocolConfig.ProtocolType"/>、<see cref="ProtocolConfig.IsActive"/>
    /// 补齐到与权威字段 <see cref="ProtocolConfig.Type"/>、<see cref="ProtocolConfig.Status"/> 一致。
    /// </summary>
    /// <remarks>
    /// 抽成一个方法是为了让创建、更新、启动、停止四条路径共用同一份派生规则，
    /// 避免任何一条路径漏改后再次产生「筛不到」的配置。
    /// 返回值用于让调用方仅在真的发生变更时才落库，避免无谓的 UPDATE 与 UpdatedAt 抖动。
    /// </remarks>
    /// <param name="config">待补齐的协议配置实体。</param>
    /// <returns>发生了字段变更返回 <c>true</c>。</returns>
    private static bool ReconcileDerivedFields(ProtocolConfig config)
    {
        var changed = false;

        var expectedProtocolType = DeriveProtocolType(config.Type);
        if (!string.Equals(config.ProtocolType, expectedProtocolType, StringComparison.Ordinal))
        {
            config.ProtocolType = expectedProtocolType;
            changed = true;
        }

        var expectedIsActive = IsRunningStatus(config.Status);
        if (config.IsActive != expectedIsActive)
        {
            config.IsActive = expectedIsActive;
            changed = true;
        }

        return changed;
    }

    #endregion

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
            // 由 Type 派生：前端与 CreateProtocolConfigRequest 都不带该字段，
            // 不在这里补齐，安圣发现/认领/下发三处 ProtocolType 等值筛选就永远命不中本行。
            ProtocolType = DeriveProtocolType(request.Type),
            Status = "inactive",
            // 必须显式置 false：ProtocolConfig.IsActive 的属性初始化器是 true，
            // 不写就会落库成「Status=inactive 但 IsActive=true」的自相矛盾行，
            // 让所有按 IsActive 的筛选把一个尚未启动的配置当成活跃配置。
            IsActive = false,
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
            // 回填真实落库值：DTO 里漏了这个字段会让调用方看到 default(false)，
            // 与列表接口（GetProtocolConfigsAsync 有映射）自相矛盾。
            IsActive = config.IsActive,
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

        // Status 是生命周期的权威字段（Start/Stop/Delete 都据它判断）。
        // 请求未携带时保持库中原值：UpdateProtocolConfigRequest.Status 没有 [Required]、默认是空串，
        // 原先无条件赋值会把一个正在运行的配置的 Status 抹成 ""，状态语义直接丢失。
        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            config.Status = request.Status;
        }

        config.Description = request.Description ?? config.Description;
        config.DeviceIds = request.DeviceIds != null
            ? JsonSerializer.Serialize(request.DeviceIds)
            : config.DeviceIds;
        config.Config = request.Config != null
            ? JsonSerializer.Serialize(request.Config)
            : config.Config;

        // IsActive / ProtocolType 一律由 Status / Type 派生，刻意不采信 request.IsActive：
        // 前端编辑弹窗把列表读到的 isActive 原样回填再提交（ProtocolManagementPage.tsx 第 582/600 行），
        // 只要库里存在历史不一致值，用户编辑一次就会把它写回去，本次 P0 会反复复发。
        // 保留 DTO 上的 IsActive 字段是为了不破坏前端契约，但它已降级为只读语义。
        ReconcileDerivedFields(config);
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
            // 回填派生后的真实值，避免调用方拿到与库中不一致的 IsActive。
            IsActive = config.IsActive,
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

        // 存量一致性补齐 —— 必须放在下面 Status=="active" 的短路之前。
        // 「Status=active 但 ProtocolType=NULL / IsActive 不匹配」的历史行一旦走到短路就原样返回，
        // 用户点多少次「启动」都修不好，只能手工改库；这正是本次 P0 的放大器。
        // 本调用是幂等的：字段本就一致时不产生任何写操作。
        if (ReconcileDerivedFields(config))
        {
            config.UpdatedAt = DateTime.UtcNow;
            await _protocolConfigRepository.UpdateAsync(config);
            await _unitOfWork.SaveChangesAsync();

            _logger?.LogInformation(
                "已补齐协议配置派生字段: Id={Id}, Type={Type}, ProtocolType={ProtocolType}, IsActive={IsActive}",
                config.Id, config.Type, config.ProtocolType, config.IsActive);
        }

        // ── 短路判活：DB 状态 + 进程内适配器「双条件」才允许短路 ──
        //
        // config.Status 是<b>数据库持久化状态</b>，而适配器实例活在单例 ProtocolAdapterFactory 的
        // 进程内字典里（ProtocolAdapterFactory.cs:45）。进程一重启，字典清空、DB 仍是 active，
        // 两者失配 —— 原先只看 Status 的短路会把真正的启动动作整个吞掉：
        // 接口返回 200，适配器却压根没起来，broker 上一条报文都收不到，
        // 运维只能先 /stop 再 /start 才能绕过（真机联调实测复现）。
        //
        // 所以只有「DB 说活 且 内存里确实有适配器」才是真·已启动，可以短路；
        // 否则必须走完整启动流程把适配器真正拉起来，即状态失配时的自愈。
        var runningAdapter = _adapterFactory.GetAdapter((int)config.Id);
        if (config.Status == "active")
        {
            if (runningAdapter != null)
            {
                return;
            }

            _logger?.LogWarning(
                "检测到协议配置状态与进程内适配器失配（DB 已 active，但适配器不在内存，通常因进程重启），" +
                "将执行恢复启动: Id={Id}, Name={Name}, Type={Type}, Status={Status}",
                config.Id, config.Name, config.Type, config.Status);
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

            // 更新状态：Status 与 IsActive 是同一生命周期状态的两种表示，必须同步落库。
            // 消费侧（AnShengDiscoveryService 扫描/认领、AnShengCommandService 下发）筛的是
            // IsActive + ProtocolType，只改 Status 等于没启动。
            config.Status = "active";
            config.IsActive = true;
            config.ProtocolType = DeriveProtocolType(config.Type);
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

        // 与 StartProtocolAsync 对称：先补齐再短路。
        // 否则「Status=inactive 但 IsActive=true」的残留行会被短路挡住，
        // 一个已停止的配置会被消费侧当作活跃适配器持续选中。
        if (ReconcileDerivedFields(config))
        {
            config.UpdatedAt = DateTime.UtcNow;
            await _protocolConfigRepository.UpdateAsync(config);
            await _unitOfWork.SaveChangesAsync();

            _logger?.LogInformation(
                "已补齐协议配置派生字段: Id={Id}, Type={Type}, ProtocolType={ProtocolType}, IsActive={IsActive}",
                config.Id, config.Type, config.ProtocolType, config.IsActive);
        }

        // ── 短路判活：与 StartProtocolAsync 对称的「双条件」判断 ──
        //
        // 只看 Status=="inactive" 就短路，会在「DB 说停、适配器却还留在内存」时把停止请求吞掉
        // （成因：启动中途抛异常导致 Status 没落到 active、或外部直接改库）。
        // 结果是适配器泄漏在进程里继续连着 broker 收数据、继续往采集管道灌，
        // 而管理界面显示它已经停止 —— 一个查不出来源的「幽灵数据源」。
        //
        // 所以只有「DB 说停 且 内存里确实没有适配器」才允许短路；
        // 否则走完整停止流程，把残留适配器连同其事件订阅一并释放。
        var residualAdapter = _adapterFactory.GetAdapter((int)config.Id);
        if (config.Status == "inactive")
        {
            if (residualAdapter == null)
            {
                return;
            }

            _logger?.LogWarning(
                "检测到协议配置状态与进程内适配器失配（DB 已 inactive，但适配器仍在内存），" +
                "将执行残留适配器清理: Id={Id}, Name={Name}, Type={Type}, Status={Status}",
                config.Id, config.Name, config.Type, config.Status);
        }

        try
        {
            // ═════════════ P2: 反注册 DataReceived 事件订阅 ═════════════
            UnsubscribeAdapterDataReceived((int)config.Id);

            // 释放协议适配器
            _adapterFactory.ReleaseAdapter((int)config.Id);
            _logger?.LogInformation("协议已停止: {Name}, Type={Type}", config.Name, config.Type);

            // 更新状态：与启动路径对称，Status 与 IsActive 同步回落，
            // 顺带再派生一次 ProtocolType，保证停止动作也具备自愈能力。
            config.Status = "inactive";
            config.IsActive = false;
            config.ProtocolType = DeriveProtocolType(config.Type);
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
        lock (_subscriptionLock)
        {
            if (_activeSubscriptions.TryGetValue(configId, out var existing))
            {
                // ① 同一个适配器实例上已有订阅 —— 真正的重复调用，直接返回。
                //    放宽 Start 短路后，「Status 已是 active 再走一遍完整启动流程」会真实发生，
                //    这里若不拦住，同一份报文会被处理两次：DeviceDataRecord 重复落库，
                //    且 OnProtocolAdapterDataReceived 中的 OnDeviceOnlineAsync（第 649 行 await /
                //    第 665 行 fire-and-forget）会被重复触发，直接加剧安圣设备已知的并发来源。
                if (ReferenceEquals(existing.Adapter, adapter))
                {
                    _logger?.LogDebug(
                        "协议适配器数据事件已订阅，跳过重复订阅: ConfigId={ConfigId}, ProtocolType={ProtocolType}",
                        configId, adapter.ProtocolType);
                    return;
                }

                // ② 登记的是<b>旧适配器实例</b>（适配器被释放后重建，如进程内 stop→start）。
                //    必须从旧实例上摘掉 handler 再挂到新实例，否则旧实例若仍被其他引用持有，
                //    它的 handler 会继续往采集管道灌数据。
                existing.Adapter.DataReceived -= existing.Handler;
                _activeSubscriptions.Remove(configId);

                _logger?.LogInformation(
                    "检测到协议适配器实例已重建，已解绑旧实例事件订阅: ConfigId={ConfigId}", configId);
            }

            EventHandler<DeviceDataReceivedEventArgs> handler = async (sender, e) =>
            {
                await OnProtocolAdapterDataReceived(e, appCode);
            };

            adapter.DataReceived += handler;
            _activeSubscriptions[configId] = new AdapterSubscription(adapter, handler);

            _logger?.LogInformation(
                "已订阅协议适配器数据事件: ConfigId={ConfigId}, ProtocolType={ProtocolType}",
                configId, adapter.ProtocolType);
        }
    }

    /// <summary>
    /// 取消指定协议配置的事件订阅。
    /// </summary>
    /// <remarks>
    /// 对「本来就没有订阅」的 configId 调用是安全的无操作（<c>TryGetValue</c> 落空即返回），
    /// 因此停止流程中无需先判存在性。
    /// </remarks>
    /// <param name="configId">协议配置 Id。</param>
    private void UnsubscribeAdapterDataReceived(int configId)
    {
        lock (_subscriptionLock)
        {
            if (!_activeSubscriptions.TryGetValue(configId, out var subscription))
            {
                return;
            }

            // 用登记时保存的适配器引用反注册，而不是 GetAdapter(configId) 重新取：
            // 走到这里时适配器可能已被释放（取回 null，handler 就永远摘不掉）
            // 或已被重建（取回另一个实例，-= 作用在错误对象上，静默无效）。
            subscription.Adapter.DataReceived -= subscription.Handler;
            _activeSubscriptions.Remove(configId);

            _logger?.LogInformation("已取消协议适配器数据事件订阅: ConfigId={ConfigId}", configId);
        }
    }

    /// <summary>
    /// 【测试专用】解绑并清空 <see cref="_activeSubscriptions"/>，把这块进程级静态状态复位到「进程刚启动」。
    /// </summary>
    /// <remarks>
    /// 【为什么生产代码里要开这个口子】
    /// <see cref="_activeSubscriptions"/> 是 <c>static</c>（理由见其自身注释：必须与单例工厂里的
    /// 适配器同生命周期）。代价是它<b>不随 DI 作用域、也不随 TestServer 重建而清空</b>，
    /// 于是集成测试里它成了第 5 处跨用例存活的进程级状态：
    /// 用例 A 启过协议后登记表里就留着 <c>configId → (DefaultAdapter, handlerA)</c>，
    /// 用例 B 再启同一个 configId 时命中「① 同实例跳过」分支，B 想验证的挂载动作根本没发生 ——
    /// 症状是「B 单跑绿、跟在 A 后面跑红」，且失败点在 B 里看不出成因。
    /// 测试侧无法用反射稳妥地摘 handler（记的是私有 record 的字段），故由生产代码提供入口，
    /// 与 <c>AnShengMqttProtocolAdapter.ClearDeviceKinds()</c> 的既有做法保持一致。
    ///
    /// 【为什么必须先解绑再清表，不能只 Clear()】
    /// 集成测试的录制适配器是 TestServer 级单例（<c>FakeProtocolAdapterFactory.DefaultAdapter</c>），
    /// 它的 <c>DataReceived</c> 调用列表不会随用例结束而清空。
    /// 若这里只 <c>Clear()</c> 登记表而不 <c>-=</c>，handlerA 会永远挂在那个适配器上；
    /// 下个用例再启动时因表已空而走「③ 无登记挂载」分支又挂一个 handlerB，
    /// 一条报文被处理 N 次 —— 恰好是本次加固要消灭的「幽灵订阅」，反倒被清理器亲手制造出来。
    /// 所以清理的语义必须是「反注册 + 清表」，与 <see cref="UnsubscribeAdapterDataReceived"/> 一致。
    ///
    /// 【为什么单条失败不抛】
    /// 适配器可能已被释放/替换，个别 <c>-=</c> 失败不应让剩余条目漏清，
    /// 否则一次异常就把「清干净」退化成「清了一半」，比不清更难排查。
    /// </remarks>
    internal static void ResetActiveSubscriptions()
    {
        lock (_subscriptionLock)
        {
            foreach (var subscription in _activeSubscriptions.Values)
            {
                try
                {
                    subscription.Adapter.DataReceived -= subscription.Handler;
                }
                catch (Exception)
                {
                    // 失败开放：继续清理其余条目，保证登记表最终一定被清空。
                }
            }

            _activeSubscriptions.Clear();
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
            // 决策 B-2 逃生开关锚点：若未来需按事件标记抑制本桥落库，
            // 在此判断 ShouldSkipLegacyBridge && 本报文来自事件管道，再跳过下方调用。
            // 当前默认 false，不抑制。
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
