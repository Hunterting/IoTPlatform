// B1 最后一块：设备指令 + 摄像头 + 网关/隧道写端点归属收口模式测试（设计 §4.2 / §5.2）。
//
// 【核心断言】越权 403 且副作用计数 = 0：
//   - 摄像头：EzvizService 一次都不调用（外部 API 零请求）；EzvizService 自身也用真实 HTTP 替身断言 0 次请求。
//   - 网关/隧道：Start/Stop/Connect/Disconnect 一次都不进入 Service。
//   - 指令：Send/SendBatch/Cancel/Retry 一次都不进入 Service；真实 DeviceCommandService 不发 MQTT、不落指令。
//   - DeviceCommandService:559 遗留裸通道：显式把 principal 传给 AnSheng 6 参重载（越权零下发）。
// 【零回归】授权内 200、下级 Single 自身客户可用、超管 All 全量放行。

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
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
using IoTPlatform.Helpers;
using IoTPlatform.Models;
using IoTPlatform.Services;
using IoTPlatform.Services.Guards;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace IoTPlatform.AnSheng.Tests;

/// <summary>
/// B1 M4/M7 设备域模式测试：真实范围服务 + 真实 Guard + 录制副作用替身。
/// </summary>
public sealed class PatternM4M7_DeviceScopeTests : IDisposable
{
    private const long CustomerOne = 1;
    private const long CustomerTwo = 2;
    private const long AdminId = 10;
    private const string AppOne = "customer_001";
    private const string AppTwo = "customer_002";

    private readonly AppDbContext _db;
    private readonly CustomerScopeService _scope;
    private readonly DeviceAccessGuard _deviceGuard;
    private readonly EzvizScopeGuard _ezvizGuard;
    private readonly CustomerScopeGuard _scopeGuard;

    /// <summary>装配 InMemory 数据、真实 scope/guard，授权 admin 仅拥有客户 1。</summary>
    public PatternM4M7_DeviceScopeTests()
    {
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"m4m7-device-scope-{Guid.NewGuid():N}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

        foreach (var (id, appCode) in new[] { (CustomerOne, AppOne), (CustomerTwo, AppTwo) })
        {
            _db.Customers.Add(new Customer
            {
                Id = id,
                Name = $"客户{id}",
                Code = $"C{id:000}",
                AppCode = appCode,
                Status = "active",
                MaxDeviceCount = 100,
                MaxUserCount = 100,
                MaxAreaCount = 100,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }

        _db.Devices.AddRange(
            new Device { Id = 101, AppCode = AppOne, Name = "设备101", SerialNumber = "IMEI101", Status = "online", ProtocolConfigId = 1 },
            new Device { Id = 102, AppCode = AppTwo, Name = "设备102", SerialNumber = "IMEI102", Status = "online", ProtocolConfigId = 1 });

        _db.Areas.AddRange(
            new Area { Id = 501, Name = "区域501", CustomerId = CustomerOne, AppCode = AppOne },
            new Area { Id = 502, Name = "区域502", CustomerId = CustomerTwo, AppCode = AppTwo });

        _db.Cameras.AddRange(
            new Camera { Id = 401, Name = "cam1", StreamUrl = "ezviz:SERIAL1:1", AppCode = AppOne, Protocol = "hls" },
            new Camera { Id = 402, Name = "cam2", StreamUrl = "ezviz:SERIAL2:1", AppCode = AppTwo, Protocol = "hls" });

        _db.Gateways.AddRange(
            new Gateway { Id = 201, Name = "gw1", AppCode = AppOne, SourceProtocol = "mqtt", TargetProtocol = "http", IsActive = true },
            new Gateway { Id = 202, Name = "gw2", AppCode = AppTwo, SourceProtocol = "mqtt", TargetProtocol = "http", IsActive = true });

        _db.Tunnels.AddRange(
            new Tunnel { Id = 301, Name = "tn1", AppCode = AppOne, IsActive = true },
            new Tunnel { Id = 302, Name = "tn2", AppCode = AppTwo, IsActive = true });

        _db.DeviceCommands.AddRange(
            NewCommand("cmd-c1", AppOne, CommandStatus.Failed),
            NewCommand("cmd-c2", AppTwo, CommandStatus.Failed));

        _db.SaveChanges();

        var grants = new UserCustomerRepository(_db);
        grants.SyncAsync(AdminId, new[] { CustomerOne }, null).GetAwaiter().GetResult();
        _db.SaveChanges();
        _db.ChangeTracker.Clear();

        _scope = new CustomerScopeService(grants, new CustomerRepository(_db));
        _deviceGuard = new DeviceAccessGuard(_db, _scope);
        _ezvizGuard = new EzvizScopeGuard(_db, _scope);
        _scopeGuard = new CustomerScopeGuard(_scope);
    }

    /// <inheritdoc />
    public void Dispose() => _db.Dispose();

    private static DeviceCommand NewCommand(string commandId, string appCode, CommandStatus status) => new()
    {
        CommandId = commandId,
        AppCode = appCode,
        DeviceId = 101,
        CommandType = "test",
        Status = status,
        RetryCount = 0,
        MaxRetries = 3,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };

    private static ControllerContext Ctx(ClaimsPrincipal principal) => new()
    {
        HttpContext = new DefaultHttpContext { User = principal }
    };

    private static ClaimsPrincipal Admin() =>
        ScopePrincipals.Multi(AdminId, CustomerOne, CustomerTwo); // claim 声称两客户，DB 授权只有客户1

    /// <summary>兼容 Ok(ApiResponse) 与直接返回 ApiResponse 两种控制器写法，取业务码。</summary>
    private static int Code<T>(ActionResult<ApiResponse<T>> action)
        => ExtractCode(action.Value, action.Result);

    /// <summary>无泛型数据的 ApiResponse 业务码。</summary>
    private static int Code(ActionResult<ApiResponse> action)
        => ExtractCode(action.Value, action.Result);

    /// <summary>历史控制器存在 ApiResponse&lt;object&gt; 与 ApiResponse&lt;T&gt; 混用，按运行时类型取 Code。</summary>
    private static int ExtractCode(object? value, IActionResult? result)
    {
        var payload = value ?? ((OkObjectResult)result!).Value!;
        return (int)payload.GetType().GetProperty("Code")!.GetValue(payload)!;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // M7 摄像头：跨租户查看/绑定/操控 → 403 且外部 API 零调用
    // ═══════════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "M7-01 摄像头跨租户查看/取流 → Code=403 且 EzvizService 调用数=0")]
    public async Task Camera_CrossTenant_ViewAndLiveAddress_AreForbidden_WithoutEzvizCall()
    {
        var svc = RecordingProxy<IEzvizService>.Create();
        var controller = NewEzvizController(svc.Interface, Admin());

        var cameras = await controller.GetCameras("SERIAL2");
        var live = await controller.GetLiveAddress(new EzvizLiveAddressRequest { DeviceSerial = "SERIAL2" });

        Assert.Equal(403, Code(cameras));
        Assert.Equal(403, Code(live));
        Assert.Equal(0, svc.CallCount);
    }

    [Fact(DisplayName = "M7-02 摄像头跨租户云台/关加密/切编码 → Code=403 且 EzvizService 调用数=0")]
    public async Task Camera_CrossTenant_PtzEncryptEncode_AreForbidden_WithoutEzvizCall()
    {
        var svc = RecordingProxy<IEzvizService>.Create();
        var controller = NewEzvizController(svc.Interface, Admin());

        var ptz = await controller.PtzStart(new EzvizPtzRequest { DeviceSerial = "SERIAL2" });
        var encrypt = await controller.CloseEncryption("SERIAL2");
        var encode = await controller.SetEncodeType("SERIAL2", new SetEncodeTypeRequest { EncodeType = "H264" });

        Assert.Equal(403, Code(ptz));
        Assert.Equal(403, Code(encrypt));
        Assert.Equal(403, Code(encode));
        Assert.Equal(0, svc.CallCount);
    }

    [Fact(DisplayName = "M7-03 摄像头跨租户绑定 → Code=403 且 EzvizService 调用数=0")]
    public async Task Camera_CrossTenant_Bind_IsForbidden_WithoutEzvizCall()
    {
        var svc = RecordingProxy<IEzvizService>.Create();
        var controller = NewEzvizController(svc.Interface, Admin());

        var result = await controller.BindCamera(new EzvizBindCameraRequest
        {
            DeviceSerial = "SERIAL3",
            AreaId = 502, // 客户2 区域：claim 里有 2，但 DB 授权没有
            Name = "hijack"
        });

        Assert.Equal(403, Code(result));
        Assert.Equal(0, svc.CallCount);
        Assert.Empty(_db.Cameras.Where(c => c.StreamUrl == "ezviz:SERIAL3:1").ToList());
    }

    [Fact(DisplayName = "M7-04 授权内摄像头操控 → Code=200 且调用恰好 1 次")]
    public async Task Camera_Authorized_Ptz_Returns200_AndCallsServiceOnce()
    {
        var svc = RecordingProxy<IEzvizService>.Create();
        var controller = NewEzvizController(svc.Interface, Admin());

        var result = await controller.PtzStart(new EzvizPtzRequest { DeviceSerial = "SERIAL1" });

        Assert.Equal(200, Code(result));
        Assert.Equal(1, svc.CallCount);
        Assert.Equal("PtzStartAsync", svc.LastMethod);
    }

    [Fact(DisplayName = "M7-05 EzvizService 显式 allowedAppCodes 过滤：越权外部 HTTP 请求数=0")]
    public async Task EzvizService_ResolveTenantCamera_UsesExplicitAppCodes()
    {
        var handler = new CountingHandler();
        var service = new EzvizService(
            Options.Create(new EzvizOptions { AppKey = "k", AppSecret = "s", BaseUrl = "https://example.invalid" }),
            new StubHttpClientFactory(handler),
            NullLogger<EzvizService>.Instance,
            _db,
            null!,
            _ezvizGuard,
            _scope);

        var admin = Admin();
        await Assert.ThrowsAsync<ForbiddenAccessException>(() => service.GetCamerasAsync("SERIAL2", admin));
        Assert.Equal(0, handler.RequestCount); // guard 在 token/get 之前拒绝

        await service.GetCamerasAsync("SERIAL1", admin);
        Assert.True(handler.RequestCount > 0); // 授权内才真正访问萤石
    }

    [Fact(DisplayName = "M7-06 下级 Single 自身摄像头可用、他人 403")]
    public async Task Camera_SingleScope_OwnAllowed_OtherForbidden()
    {
        var svc = RecordingProxy<IEzvizService>.Create();
        var subordinate = ScopePrincipals.Single(CustomerTwo, AppTwo);
        var controller = NewEzvizController(svc.Interface, subordinate);

        var own = await controller.PtzStop(new EzvizPtzRequest { DeviceSerial = "SERIAL2" });
        var other = await controller.PtzStop(new EzvizPtzRequest { DeviceSerial = "SERIAL1" });

        Assert.Equal(200, Code(own));
        Assert.Equal(403, Code(other));
        Assert.Equal(1, svc.CallCount);
    }

    [Fact(DisplayName = "M7-07 超管 All 可操控任意租户摄像头")]
    public async Task Camera_SuperAdmin_CanOperateAnySerial()
    {
        var svc = RecordingProxy<IEzvizService>.Create();
        var controller = NewEzvizController(svc.Interface, ScopePrincipals.All());

        var result = await controller.Capture(new EzvizCaptureRequest { DeviceSerial = "SERIAL2" });

        Assert.Equal(200, Code(result));
        Assert.Equal(1, svc.CallCount);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 网关 / 隧道：越权启停 0 次副作用
    // ═══════════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "M4-12 网关跨租户启停 → Code=403 且 Start/Stop 调用数=0")]
    public async Task Gateway_CrossTenant_StartStop_AreForbidden_WithoutServiceCall()
    {
        var svc = RecordingProxy<IGatewayService>.Create();
        var controller = new GatewaysController(svc.Interface, _db, _scope, _scopeGuard)
        {
            ControllerContext = Ctx(Admin())
        };

        var start = await controller.StartGateway(202);
        var stop = await controller.StopGateway(202);
        var update = await controller.UpdateGateway(202, new UpdateGatewayRequest { Name = "hijack" });
        var delete = await controller.DeleteGateway(202);

        Assert.Equal(403, Code(start));
        Assert.Equal(403, Code(stop));
        Assert.Equal(403, Code(update));
        Assert.Equal(403, Code(delete));
        Assert.Equal(0, svc.CallCount);
    }

    [Fact(DisplayName = "M4-13 网关授权内 + 超管全量 → Code=200 且启停调用正常")]
    public async Task Gateway_AuthorizedAndSuperAdmin_StartStop_Returns200()
    {
        var svc = RecordingProxy<IGatewayService>.Create();
        var adminController = new GatewaysController(svc.Interface, _db, _scope, _scopeGuard)
        {
            ControllerContext = Ctx(Admin())
        };
        var superController = new GatewaysController(svc.Interface, _db, _scope, _scopeGuard)
        {
            ControllerContext = Ctx(ScopePrincipals.All())
        };

        var own = await adminController.StartGateway(201);
        var all = await superController.StartGateway(202);

        Assert.Equal(200, Code(own));
        Assert.Equal(200, Code(all));
        Assert.Equal(2, svc.CallCount);
    }

    [Fact(DisplayName = "M4-14 隧道跨租户连接/断开 → Code=403 且 Connect/Disconnect 调用数=0")]
    public async Task Tunnel_CrossTenant_ConnectDisconnect_AreForbidden_WithoutServiceCall()
    {
        var svc = RecordingProxy<ITunnelService>.Create();
        var controller = new TunnelsController(svc.Interface, _db, _scope, _scopeGuard)
        {
            ControllerContext = Ctx(Admin())
        };

        var connect = await controller.ConnectTunnel(302);
        var disconnect = await controller.DisconnectTunnel(302);

        Assert.Equal(403, Code(connect));
        Assert.Equal(403, Code(disconnect));
        Assert.Equal(0, svc.CallCount);
    }

    [Fact(DisplayName = "M4-15 下级 Single 隧道零回归 + 跨租户 403")]
    public async Task Tunnel_SingleScope_OwnAllowed_OtherForbidden()
    {
        var svc = RecordingProxy<ITunnelService>.Create();
        var controller = new TunnelsController(svc.Interface, _db, _scope, _scopeGuard)
        {
            ControllerContext = Ctx(ScopePrincipals.Single(CustomerOne, AppOne))
        };

        var own = await controller.ConnectTunnel(301);
        var other = await controller.ConnectTunnel(302);

        Assert.Equal(200, Code(own));
        Assert.Equal(403, Code(other));
        Assert.Equal(1, svc.CallCount);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 设备指令：send/batch/cancel/retry 越权 403 且零下发
    // ═══════════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "M4-16 指令跨租户 send/batch/cancel/retry → Code=403 且指令服务调用数=0")]
    public async Task DeviceCommand_CrossTenant_AllWrites_AreForbidden_WithoutServiceCall()
    {
        var svc = RecordingProxy<IDeviceCommandService>.Create();
        svc.Returns["SendCommandAsync"] = _ => Task.FromResult(new DeviceCommandResponse { Success = true });
        svc.Returns["SendBatchCommandsAsync"] = _ => Task.FromResult(new List<DeviceCommandResponse>());
        svc.Returns["CancelCommandAsync"] = _ => Task.FromResult(true);
        svc.Returns["RetryCommandAsync"] = _ => Task.FromResult(true);
        var controller = NewCommandController(svc.Interface, Admin());

        var send = await controller.SendCommand(new SendDeviceCommandRequest { DeviceId = 102, CommandType = "test" });
        var batch = await controller.SendBatchCommands(new SendBatchDeviceCommandRequest
        {
            DeviceIds = new List<long> { 101, 102 }, CommandType = "test"
        });
        var cancel = await controller.CancelCommand("cmd-c2");
        var retry = await controller.RetryCommand("cmd-c2");

        Assert.Equal(403, Code(send));
        Assert.Equal(403, Code(batch));
        Assert.Equal(403, Code(cancel));
        Assert.Equal(403, Code(retry));
        Assert.Equal(0, svc.CallCount);
    }

    [Fact(DisplayName = "M4-17 指令授权内 send/cancel/retry → Code=200 且调用成功")]
    public async Task DeviceCommand_Authorized_Writes_Return200()
    {
        var svc = RecordingProxy<IDeviceCommandService>.Create();
        svc.Returns["SendCommandAsync"] = _ => Task.FromResult(new DeviceCommandResponse { Success = true });
        svc.Returns["CancelCommandAsync"] = _ => Task.FromResult(true);
        svc.Returns["RetryCommandAsync"] = _ => Task.FromResult(true);
        var controller = NewCommandController(svc.Interface, Admin());

        var send = await controller.SendCommand(new SendDeviceCommandRequest { DeviceId = 101, CommandType = "test" });
        var cancel = await controller.CancelCommand("cmd-c1");
        var retry = await controller.RetryCommand("cmd-c1");

        Assert.Equal(200, Code(send));
        Assert.Equal(200, Code(cancel));
        Assert.Equal(200, Code(retry));
        Assert.Equal(3, svc.CallCount);
    }

    [Fact(DisplayName = "M4-18 DeviceCommandService 越权 → 抛 Forbidden 且 MQTT 发布数=0、无指令落库")]
    public async Task DeviceCommandService_CrossTenant_ThrowsBeforeMqttAndDb()
    {
        var mqtt = new RecordingMqtt();
        var sut = new DeviceCommandService(
            _db, mqtt, NullLogger<DeviceCommandService>.Instance,
            anShengCommandService: null, deviceGuard: _deviceGuard, scope: _scope, scopeGuard: _scopeGuard);
        var before = _db.DeviceCommands.Count();

        await Assert.ThrowsAsync<ForbiddenAccessException>(() =>
            sut.SendCommandAsync(
                new SendDeviceCommandRequest { DeviceId = 102, CommandType = "test" },
                AppTwo, AdminId, "admin", Admin()));

        Assert.Equal(0, mqtt.SendCount);
        Assert.Equal(before, _db.DeviceCommands.Count());
    }

    [Fact(DisplayName = "M4-19 :559 遗留裸通道 → 显式传 principal 到 AnSheng 6 参重载，越权零下发")]
    public async Task DeviceCommandService_AnShengBranch_ForwardsPrincipal()
    {
        var anSheng = RecordingProxy<IAnShengCommandService>.Create();
        anSheng.Returns["SendCommandAsync"] = _ => Task.FromResult(new AnShengCommandResponse { Success = true });
        var mqtt = new RecordingMqtt();
        var sut = new DeviceCommandService(
            _db, mqtt, NullLogger<DeviceCommandService>.Instance,
            anShengCommandService: anSheng.Interface, deviceGuard: _deviceGuard,
            scope: _scope, scopeGuard: _scopeGuard);

        var principal = Admin();
        var ok = await sut.SendCommandAsync(
            new SendDeviceCommandRequest { DeviceId = 101, CommandType = "action" },
            AppOne, AdminId, "admin", principal);
        Assert.True(ok.Success, ok.ErrorMessage);
        Assert.Equal(1, anSheng.CallCount);
        Assert.Equal("SendCommandAsync", anSheng.LastMethod);
        Assert.Same(principal, anSheng.LastArgs![3]); // deviceId, method, parameters, principal, ct, commandId
        Assert.Equal(6, anSheng.LastArgs!.Length);
        Assert.Equal(0, mqtt.SendCount);

        // 越权设备：Guard 在 AnSheng 下发之前抛出，零调用。
        await Assert.ThrowsAsync<ForbiddenAccessException>(() =>
            sut.SendCommandAsync(
                new SendDeviceCommandRequest { DeviceId = 102, CommandType = "action" },
                AppTwo, AdminId, "admin", principal));
        Assert.Equal(1, anSheng.CallCount);

        // 旧签名内部通道保持 5 参裸通道，零行为变化。
        await sut.SendCommandAsync(
            new SendDeviceCommandRequest { DeviceId = 101, CommandType = "action" },
            AppOne, AdminId, "admin");
        Assert.Equal(2, anSheng.CallCount);
        Assert.Equal(5, anSheng.LastArgs!.Length);
    }

    // ── 夹具 ────────────────────────────────────────────────────────────────

    private DeviceCommandsController NewCommandController(IDeviceCommandService svc, ClaimsPrincipal principal) =>
        new(svc, _deviceGuard, _scopeGuard, _scope, _db) { ControllerContext = Ctx(principal) };

    private EzvizController NewEzvizController(IEzvizService svc, ClaimsPrincipal principal) =>
        new(svc, NullLogger<EzvizController>.Instance, _ezvizGuard, _deviceGuard, _scope, _db)
        {
            ControllerContext = Ctx(principal)
        };

    /// <summary>
    /// 通用录制替身：统计调用次数、方法与实参；支持按方法名配置返回值。
    /// 越权断言只检查 CallCount，保证“403 且零副作用”不是 mock 出来的假阳性。
    /// </summary>
    public class RecordingProxy<T> : DispatchProxy where T : class
    {
        public int CallCount { get; private set; }
        public string? LastMethod { get; private set; }
        public object?[]? LastArgs { get; private set; }
        public Dictionary<string, Func<object?[]?, object?>> Returns { get; } = new();
        public Func<Exception>? Throw { get; set; }

        /// <summary>代理对象本身作为接口使用，同时保留录制计数（避免元组解构样板代码）。</summary>
        public T Interface => (T)(object)this;

        public static RecordingProxy<T> Create()
            => (RecordingProxy<T>)(object)DispatchProxy.Create<T, RecordingProxy<T>>();

        /// <inheritdoc />
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            CallCount++;
            LastMethod = targetMethod?.Name;
            LastArgs = args;
            if (Throw != null) throw Throw();
            if (targetMethod?.Name != null && Returns.TryGetValue(targetMethod.Name, out var factory))
            {
                return factory(args);
            }

            var returnType = targetMethod?.ReturnType;
            if (returnType == typeof(Task)) return Task.CompletedTask;
            if (returnType != null && returnType.IsGenericType
                && returnType.GetGenericTypeDefinition() == typeof(Task<>))
            {
                var payloadType = returnType.GetGenericArguments()[0];
                var tcsType = typeof(TaskCompletionSource<>).MakeGenericType(payloadType);
                var tcs = Activator.CreateInstance(tcsType)!;
                var payload = payloadType.IsValueType ? Activator.CreateInstance(payloadType) : null;
                tcsType.GetMethod("SetResult", new[] { payloadType })!.Invoke(tcs, new[] { payload });
                return tcsType.GetProperty("Task")!.GetValue(tcs);
            }

            return null;
        }
    }

    /// <summary>录制 MQTT 下发次数；越权场景必须为 0。</summary>
    private sealed class RecordingMqtt : IMqttClientService
    {
        public int SendCount { get; private set; }
        public event EventHandler<DeviceDataEventArgs>? OnDataReceived { add { } remove { } }
        public event EventHandler<CommandResponseEventArgs>? OnCommandResponse { add { } remove { } }
        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StopAsync() => Task.CompletedTask;
        public Task SubscribeToTopicAsync(string topic) => Task.CompletedTask;
        public Task PublishMessageAsync(string topic, string payload) => Task.CompletedTask;

        public Task SendDeviceCommandAsync(
            string appCode, long deviceId, string commandId, string commandType, string? parameters)
        {
            SendCount++;
            return Task.CompletedTask;
        }
    }

    /// <summary>统计萤石外部 HTTP 请求；越权场景必须为 0。</summary>
    private sealed class CountingHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            var body = request.RequestUri!.AbsolutePath.Contains("token")
                ? "{\"code\":\"200\",\"data\":{\"accessToken\":\"t\",\"expireTime\":9999999999999}}"
                : "{\"code\":\"200\",\"data\":[]}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body)
            });
        }
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        public StubHttpClientFactory(HttpMessageHandler handler) => _handler = handler;
        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }
}
