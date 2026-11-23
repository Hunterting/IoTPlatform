// B1 / M4 + M11：AnSheng 命令族单点收口测试（方案 C：显式 principal 重载贯穿业务服务层）。
//
// 【核心验收（设计 §4.2）】跨租户 deviceId 100% 403，且 mock 断言 MQTT publish 次数为 0。
// 【零回归】授权内 200 + publish 1 次；下级 Single 自身客户可下发、他人 403；
//           内部通道（旧签名）保持原行为。
// 【按行为鉴权】名义 GET 的 GetCalParams 与写端点共用同一收口，纯查询 GET 不传 principal。

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using IoTPlatform.Configuration;
using IoTPlatform.Controllers;
using IoTPlatform.Data;
using IoTPlatform.Data.Repositories.Implementations;
using IoTPlatform.DTOs.Requests;
using IoTPlatform.DTOs.Responses;
using IoTPlatform.Infrastructure.Protocol;
using IoTPlatform.Infrastructure.Protocol.Adapters;
using IoTPlatform.Infrastructure.Protocol.AnSheng;
using IoTPlatform.Models;
using IoTPlatform.Services;
using IoTPlatform.Services.Guards;
using IoTPlatform.Services.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace IoTPlatform.AnSheng.Tests;

/// <summary>
/// AnSheng 命令服务单点收口的模式测试（设计 §5.2：M4/M11 命令副作用专项）。
///
/// <para>夹具装配<b>真实</b> <see cref="DeviceAccessGuard"/> + <see cref="CustomerScopeService"/>
/// + <see cref="AnShengCommandService"/>，只有 MQTT 下行端口是录制替身，
/// 因此「越权 403 且零 publish」覆盖的是真实判定链路，不是 mock 出来的结论。</para>
/// </summary>
public sealed class PatternM4_AnShengCommandScopeTests : IDisposable
{
    private const long CustomerOne = 1;
    private const long CustomerTwo = 2;
    private const long CustomerThree = 3;
    private const long DeviceOne = 101;
    private const long DeviceTwo = 102;
    private const long DeviceThree = 103;
    private const long AdminId = 10;
    private const int ConfigId = 1;

    private readonly AppDbContext _db;
    private readonly RecordingDownlinkAdapter _adapter = new();
    private readonly AnShengCommandService _sut;
    private readonly AnShengPendingCommandStore _pending;

    /// <summary>装配 InMemory 数据库、真实范围服务/守卫与录制 MQTT 适配器。</summary>
    public PatternM4_AnShengCommandScopeTests()
    {
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"m4-cmd-scope-{Guid.NewGuid():N}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

        foreach (var id in new[] { CustomerOne, CustomerTwo, CustomerThree })
        {
            _db.Customers.Add(new Customer
            {
                Id = id,
                Name = $"客户{id}",
                Code = $"C{id:000}",
                AppCode = $"customer_{id:000}",
                Status = "active",
                MaxDeviceCount = 100,
                MaxUserCount = 100,
                MaxAreaCount = 100,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }

        foreach (var (id, customer) in new[]
                 {
                     (DeviceOne, CustomerOne), (DeviceTwo, CustomerTwo), (DeviceThree, CustomerThree)
                 })
        {
            var appCode = $"customer_{customer:000}";
            var imei = $"8645360729499{customer:00}";
            _db.Devices.Add(new Device
            {
                Id = id,
                AppCode = appCode,
                Name = $"设备{id}",
                SerialNumber = imei,
                ProtocolConfigId = ConfigId,
                Status = "online"
            });
            _db.AnShengDeviceProfiles.Add(new AnShengDeviceProfile
            {
                AppCode = appCode,
                Imei = imei,
                DeviceId = id,
                Kind = AnShengDeviceKind.Switch4G,
                KindSource = AnShengKindSource.Manual,
                Version = "AnSheng_Switch_V4.0.20",
                SlotAmount = 4
            });
        }

        _db.SaveChanges();

        var grants = new UserCustomerRepository(_db);
        grants.SyncAsync(AdminId, new[] { CustomerOne }, 1).GetAwaiter().GetResult();
        _db.SaveChanges();
        _db.ChangeTracker.Clear();

        var scope = new CustomerScopeService(grants, new CustomerRepository(_db));
        var guard = new DeviceAccessGuard(_db, scope);
        _pending = new AnShengPendingCommandStore(NullLogger<AnShengPendingCommandStore>.Instance);

        _sut = new AnShengCommandService(
            _db,
            new StubAdapterFactory(_adapter),
            new StubScopeFactory(),
            new AnShengDeviceProfileService(_db),
            new AnShengCommandGuard(),
            _pending,
            Options.Create(new AnShengCommandOptions { SweepEnabled = false }),
            NullLogger<AnShengCommandService>.Instance,
            guard);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _pending.ClearAll();
        _db.Dispose();
    }

    // ─────────────────────────────────────────────────────────────
    // 1. 核心验收：跨租户 403 + 零 MQTT publish
    // ─────────────────────────────────────────────────────────────

    /// <summary>M4-01：跨租户下发 → 403，零 publish、零在途、零命令落库。</summary>
    [Fact(DisplayName = "M4-01 跨租户 deviceId → Forbidden 且 MQTT publish=0")]
    public async Task CrossTenant_ThrowsForbidden_AndPublishesNothing()
    {
        // claim 声称管到客户 3，但 DB 授权只有客户 1 —— 快照不参与判定。
        var admin = ScopePrincipals.Multi(AdminId, CustomerOne, CustomerTwo, CustomerThree);

        var ex = await Assert.ThrowsAsync<ForbiddenAccessException>(() =>
            _sut.SendCommandAsync(DeviceThree, "action", ActionParameters(), admin));

        Assert.Contains("无权", ex.Message);
        Assert.Equal(0, _adapter.PublishCount);
        Assert.Equal(0, _pending.Count);
        Assert.Empty(_db.Set<AnShengCommandRecord>().ToList());
    }

    /// <summary>M4-02：授权内下发成功，publish 恰好 1 次。</summary>
    [Fact(DisplayName = "M4-02 授权内 deviceId → 下发成功且 MQTT publish=1")]
    public async Task AuthorizedDevice_PublishesExactlyOnce()
    {
        var admin = ScopePrincipals.Multi(AdminId, CustomerOne);

        var response = await _sut.SendCommandAsync(DeviceOne, "action", ActionParameters(), admin);

        Assert.True(response.Success, response.ErrorMessage);
        Assert.NotNull(response.FrameId);
        Assert.Equal(1, _adapter.PublishCount);
        Assert.Equal(DeviceOne, _adapter.LastDeviceId);
        Assert.Single(_db.Set<AnShengCommandRecord>().ToList());
    }

    /// <summary>M4-03：新重载禁止 principal=null，且必须在任何 publish 之前失败。</summary>
    [Fact(DisplayName = "M4-03 新重载 principal=null → Unauthorized 且零 publish")]
    public async Task ExplicitOverload_WithNullPrincipal_ThrowsUnauthorized()
    {
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _sut.SendCommandAsync(DeviceOne, "action", ActionParameters(), null));

        Assert.Equal(0, _adapter.PublishCount);
        Assert.Empty(_db.Set<AnShengCommandRecord>().ToList());
    }

    // ─────────────────────────────────────────────────────────────
    // 2. 下级零回归 / 超管全量 / 内部通道
    // ─────────────────────────────────────────────────────────────

    /// <summary>M4-04：下级 Single 自身客户可下发，他人客户 403 且零新增 publish。</summary>
    [Fact(DisplayName = "M4-04 下级 Single 自身客户 200 / 他人客户 403")]
    public async Task Subordinate_CanUseOwnDevice_ButNotOthers()
    {
        var operator002 = ScopePrincipals.Single(CustomerTwo, "customer_002");

        var own = await _sut.SendCommandAsync(DeviceTwo, "action", ActionParameters(), operator002);
        Assert.True(own.Success, own.ErrorMessage);
        Assert.Equal(1, _adapter.PublishCount);

        await Assert.ThrowsAsync<ForbiddenAccessException>(() =>
            _sut.SendCommandAsync(DeviceOne, "action", ActionParameters(), operator002));

        Assert.Equal(1, _adapter.PublishCount);
    }

    /// <summary>M4-05：超管 All 对任意租户设备仍可下发（空集合=不过滤，原则 E）。</summary>
    [Fact(DisplayName = "M4-05 超管 All 跨租户下发仍放行")]
    public async Task SuperAdmin_CanOperateAnyDevice()
    {
        var response = await _sut.SendCommandAsync(DeviceThree, "action", ActionParameters(), ScopePrincipals.All());

        Assert.True(response.Success, response.ErrorMessage);
        Assert.Equal(1, _adapter.PublishCount);
    }

    /// <summary>M4-06：旧签名内部通道（HostedService / DeviceCommandService）行为逐字不变。</summary>
    [Fact(DisplayName = "M4-06 内部通道旧签名仍可下发且零校验")]
    public async Task InternalChannel_OldSignature_StillPublishes()
    {
        var response = await _sut.SendCommandAsync(DeviceOne, "action", ActionParameters());

        Assert.True(response.Success, response.ErrorMessage);
        Assert.Equal(1, _adapter.PublishCount);
    }

    /// <summary>M4-07：ConfigureAutoReport / Reboot 的 principal 重载走同一收口。</summary>
    [Fact(DisplayName = "M4-07 自动上报与重启的 principal 重载同收口")]
    public async Task WrapperOverloads_AlsoGuard()
    {
        var admin = ScopePrincipals.Multi(AdminId, CustomerOne);

        await Assert.ThrowsAsync<ForbiddenAccessException>(() =>
            _sut.ConfigureAutoReportAsync(DeviceThree, new IoTPlatform.Services.AnShengAutoReportSettings(), admin));
        await Assert.ThrowsAsync<ForbiddenAccessException>(() =>
            _sut.RebootDeviceAsync(DeviceThree, admin));

        Assert.Equal(0, _adapter.PublishCount);

        Assert.True((await _sut.RebootDeviceAsync(DeviceOne, admin)).Success);
        Assert.Equal(1, _adapter.PublishCount);
    }

    // ─────────────────────────────────────────────────────────────
    // 3. 显式 principal 贯穿业务服务层与 Controller
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// M4-08：业务服务（以 GetCalParams 为例）必须把 principal 原样传给命令服务新重载；
    /// 旧签名仍走内部通道，不传 principal。
    /// </summary>
    [Fact(DisplayName = "M4-08 业务服务显式转发 principal 到命令服务")]
    public async Task BusinessService_ForwardsPrincipalToCommandService()
    {
        var cmd = DispatchProxy.Create<IAnShengCommandService, CapturingProxy<IAnShengCommandService>>();
        var proxy = (CapturingProxy<IAnShengCommandService>)(object)cmd;
        proxy.ReturnValueFactory = () => Task.FromResult(new AnShengCommandResponse { Success = true });
        var dataCollection = DispatchProxy.Create<IDataCollectionService, CapturingProxy<IDataCollectionService>>();
        var energy = new AnShengEnergyService(
            _db, cmd, dataCollection,
            Options.Create(new AnShengCommandOptions()), NullLogger<AnShengEnergyService>.Instance);

        var principal = ScopePrincipals.Multi(AdminId, CustomerOne);
        await energy.GetCalParamsAsync(DeviceThree, principal);
        Assert.NotNull(proxy.LastArgs);
        Assert.Same(principal, proxy.LastArgs![3]);   // deviceId, method, parameters, principal, ct, commandId

        Assert.Equal(6, proxy.LastArgs!.Length); // deviceId, method, parameters, principal, ct, commandId

        await energy.GetCalParamsAsync(DeviceThree);
        Assert.Equal(5, proxy.LastArgs!.Length); // 旧签名 5 参：无 principal 位
        Assert.Null(proxy.LastArgs[4]);          // commandId 由编译器补 null
    }

    /// <summary>
    /// M4-09（M11）：名义 GET 的 GetCalParams 显式传 User；同 Controller 的纯查询 GET 不进入命令路径。
    /// </summary>
    [Fact(DisplayName = "M4-09 GetCalParams 显式传 User，纯查询 GET 不调用下发服务")]
    public async Task GetCalParams_PassesUser_PureQueryDoesNotCallCommandService()
    {
        var service = DispatchProxy.Create<IAnShengEnergyService, CapturingProxy<IAnShengEnergyService>>();
        var proxy = (CapturingProxy<IAnShengEnergyService>)(object)service;
        var controller = new AnShengEnergyController(service, NullLogger<AnShengEnergyController>.Instance);
        var principal = ScopePrincipals.Multi(AdminId, CustomerOne);
        controller.ControllerContext = Ctx(principal);

        await controller.GetCalParams(DeviceThree);
        Assert.NotNull(proxy.LastArgs);
        Assert.Equal("GetCalParamsAsync", proxy.LastMethodName);
        Assert.Same(principal, proxy.LastArgs![1]);   // deviceId, principal, ct

        await controller.GetStatistics(DeviceThree);
        Assert.Equal("QueryStatisticsAsync", proxy.LastMethodName); // 纯查询走独立只读方法
    }

    /// <summary>M4-10：越权异常必须被 Controller 映射为业务码 403，而不是吞成 500。</summary>
    [Fact(DisplayName = "M4-10 GetCalParams 越权 → ApiResponse.Code=403")]
    public async Task GetCalParams_Forbidden_MappedTo403()
    {
        var service = DispatchProxy.Create<IAnShengEnergyService, CapturingProxy<IAnShengEnergyService>>();
        ((CapturingProxy<IAnShengEnergyService>)(object)service).Throw = () =>
            new ForbiddenAccessException("无权操作该设备");

        var controller = new AnShengEnergyController(service, NullLogger<AnShengEnergyController>.Instance);
        controller.ControllerContext = Ctx(ScopePrincipals.Multi(AdminId, CustomerOne));

        var result = await controller.GetCalParams(DeviceThree);

        Assert.NotNull(result.Value);
        Assert.Equal(403, result.Value!.Code);
    }

    /// <summary>M4-11：其余 Controller 下发端点也显式传 User，查询端点不调用下发服务。</summary>
    [Fact(DisplayName = "M4-11 四个 Controller 下发端点显式传 User")]
    public async Task OtherControllers_PassUserOnCommandEndpoints()
    {
        var principal = ScopePrincipals.Multi(AdminId, CustomerOne);

        // WaterFlow：RequestFlowInfo(deviceId, principal, ct)
        var water = DispatchProxy.Create<IAnShengWaterFlowService, CapturingProxy<IAnShengWaterFlowService>>();
        var waterProxy = (CapturingProxy<IAnShengWaterFlowService>)(object)water;
        var waterController = new AnShengWaterFlowController(water, NullLogger<AnShengWaterFlowController>.Instance);
        waterController.ControllerContext = Ctx(principal);
        await waterController.RequestFlowInfo(DeviceOne);
        Assert.Same(principal, waterProxy.LastArgs![1]);
        await waterController.GetLatestFlowInfo(DeviceOne);
        Assert.Equal("GetLatestFlowInfoAsync", waterProxy.LastMethodName);

        // Schedule：SetTimeTasks(deviceId, slots, confirm, principal, rowVersion, ct)
        var schedule = DispatchProxy.Create<IAnShengScheduleService, CapturingProxy<IAnShengScheduleService>>();
        var scheduleProxy = (CapturingProxy<IAnShengScheduleService>)(object)schedule;
        var scheduleController = new AnShengScheduleController(schedule, NullLogger<AnShengScheduleController>.Instance);
        scheduleController.ControllerContext = Ctx(principal);
        await scheduleController.SetTimeTasks(DeviceOne, new AnShengSetTimeTasksRequest());
        Assert.Same(principal, scheduleProxy.LastArgs![3]);
        await scheduleController.GetTimeTasks(DeviceOne);
        Assert.Equal("GetTimeTasksAsync", scheduleProxy.LastMethodName);

        // Switch：直接走新重载，User 在第 4 参数位
        var commandService = DispatchProxy.Create<IAnShengCommandService, CapturingProxy<IAnShengCommandService>>();
        var commandProxy = (CapturingProxy<IAnShengCommandService>)(object)commandService;
        commandProxy.ReturnValueFactory = () => new AnShengCommandResponse { Success = true };
        var switchController = new AnShengSwitchController(
            commandService,
            schedule,
            DispatchProxy.Create<IAnShengDeviceProfileService, CapturingProxy<IAnShengDeviceProfileService>>(),
            NullLogger<AnShengSwitchController>.Instance);
        switchController.ControllerContext = Ctx(principal);
        await switchController.Action(DeviceOne, new AnShengActionRequest { SlotNum = 1, Action = "on" });
        Assert.Same(principal, commandProxy.LastArgs![3]);

        // AnShengController.ConfigureAutoReport / Reboot：必须调用带 principal 的包装重载
        var anShengController = new AnShengController(
            _db, commandService,
            DispatchProxy.Create<IAnShengDiscoveryService, CapturingProxy<IAnShengDiscoveryService>>(),
            DispatchProxy.Create<IAnShengDeviceProfileService, CapturingProxy<IAnShengDeviceProfileService>>(),
            new StubScopeFactory(),
            NullLogger<AnShengController>.Instance);
        anShengController.ControllerContext = Ctx(principal);
        await anShengController.RebootDevice(DeviceOne);
        Assert.Same(principal, commandProxy.LastArgs![1]);  // deviceId, principal, ct
    }

    // ─────────────────────────────────────────────────────────────
    // 夹具
    // ─────────────────────────────────────────────────────────────

    private static ControllerContext Ctx(ClaimsPrincipal principal) => new()
    {
        HttpContext = new DefaultHttpContext { User = principal }
    };

    private static Dictionary<string, object?>? ActionParameters() => new()
    {
        ["slotNum"] = 1,
        ["action"] = "on"
    };

    /// <summary>录制下行端口：每次 PublishAsync 自增，并保留目标设备 ID。</summary>
    private sealed class RecordingDownlinkAdapter : IProtocolAdapter, IAnShengDownlinkPort
    {
        /// <summary>publish 调用次数。</summary>
        public int PublishCount { get; private set; }

        /// <summary>最近一次 publish 的目标设备。</summary>
        public long LastDeviceId { get; private set; }

        /// <inheritdoc />
        public string ProtocolType => "ANSHENG_MQTT";

        /// <inheritdoc />
        public bool IsConnected => true;

        /// <inheritdoc />
        public int ConfigId => ConfigId;

        /// <inheritdoc />
        public Task<string> PublishAsync(long deviceId, string imei, string method,
            IReadOnlyDictionary<string, object?>? parameters, string frameId,
            CancellationToken cancellationToken = default)
        {
            PublishCount++;
            LastDeviceId = deviceId;
            return Task.FromResult(frameId);
        }

        /// <inheritdoc />
        public Task<string> SendCommandAsync(long deviceId, string serialNumber, string commandType,
            string parameters, CancellationToken cancellationToken = default)
        {
            PublishCount++;
            LastDeviceId = deviceId;
            return Task.FromResult("fallback-frame");
        }

        /// <inheritdoc />
        public Task<bool> ConnectAsync(string connectionString, CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        /// <inheritdoc />
        public Task DisconnectAsync() => Task.CompletedTask;

        /// <inheritdoc />
        public Task StartDataCollectionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        /// <inheritdoc />
        public Task StopDataCollectionAsync() => Task.CompletedTask;

        /// <inheritdoc />
        public Task ReadDataPointsAsync(long deviceId, string serialNumber, IEnumerable<string> dataPoints,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        /// <inheritdoc />
        public void Dispose()
        {
        }

        /// <inheritdoc />（测试替身不发事件）
        public event EventHandler<DeviceDataReceivedEventArgs>? DataReceived
        {
            add { }
            remove { }
        }

        /// <inheritdoc />（测试替身不发事件）
        public event EventHandler<DeviceCommandResponseEventArgs>? CommandResponse
        {
            add { }
            remove { }
        }

        /// <inheritdoc />（测试替身不发事件）
        public event EventHandler<bool>? ConnectionStateChanged
        {
            add { }
            remove { }
        }
    }

    /// <summary>只返回录制适配器的工厂替身。</summary>
    private sealed class StubAdapterFactory : IProtocolAdapterFactory
    {
        private readonly IProtocolAdapter _adapter;

        /// <summary>以指定适配器构造工厂。</summary>
        public StubAdapterFactory(IProtocolAdapter adapter) => _adapter = adapter;

        /// <inheritdoc />
        public IProtocolAdapter CreateAdapter(string protocolType, int configId) => _adapter;

        /// <inheritdoc />
        public IProtocolAdapter? GetAdapter(int configId) => _adapter;

        /// <inheritdoc />
        public void ReleaseAdapter(int configId)
        {
        }

        /// <inheritdoc />
        public void ReleaseAll()
        {
        }
    }

    /// <summary>本批测试不触发设备发现，作用域工厂仅用于满足构造签名。</summary>
    private sealed class StubScopeFactory : IServiceScopeFactory
    {
        /// <inheritdoc />
        public IServiceScope CreateScope() =>
            throw new NotSupportedException("本批用例不调用 TriggerDiscoveryAsync");
    }

    /// <summary>
    /// 用 DispatchProxy 捕获业务服务调用实参，验证 principal 被显式透传，
    /// 无需为多个接口手写十几个方法的空实现。
    /// </summary>
    /// <typeparam name="T">被代理的服务接口。</typeparam>
    public class CapturingProxy<T> : DispatchProxy where T : class
    {
        /// <summary>最近一次调用的实参数组。</summary>
        public object?[]? LastArgs { get; set; }

        /// <summary>最近一次调用的方法名。</summary>
        public string? LastMethodName { get; set; }

        /// <summary>自定义返回值工厂；默认返回该接口方法的默认任务结果。</summary>
        public Func<object?>? ReturnValueFactory { get; set; }

        /// <summary>设置后每次调用抛出该异常工厂产生的异常。</summary>
        public Func<Exception>? Throw { get; set; }

        /// <inheritdoc />
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            LastArgs = args;
            LastMethodName = targetMethod?.Name;
            if (Throw != null)
            {
                throw Throw();
            }

            if (ReturnValueFactory != null)
            {
                return ReturnValueFactory();
            }

            var returnType = targetMethod?.ReturnType;
            if (returnType == typeof(Task))
            {
                return Task.CompletedTask;
            }

            if (returnType != null && returnType.IsGenericType &&
                returnType.GetGenericTypeDefinition() == typeof(Task<>))
            {
                var payloadType = returnType.GetGenericArguments()[0];
                var payload = Activator.CreateInstance(payloadType);
                var tcsType = typeof(TaskCompletionSource<>).MakeGenericType(payloadType);
                var tcs = Activator.CreateInstance(tcsType)!;
                tcsType.GetMethod("SetResult", new[] { payloadType })!.Invoke(tcs, new[] { payload });
                return tcsType.GetProperty("Task")!.GetValue(tcs);
            }

            return null;
        }
    }
}
