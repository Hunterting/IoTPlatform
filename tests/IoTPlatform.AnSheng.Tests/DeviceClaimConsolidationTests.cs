using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using IoTPlatform.Configuration;
using IoTPlatform.Controllers;
using IoTPlatform.Data;
using IoTPlatform.DTOs.Requests;
using IoTPlatform.DTOs.Responses;
using IoTPlatform.Helpers;
using IoTPlatform.Infrastructure.Protocol;
using IoTPlatform.Infrastructure.Protocol.Adapters;
using IoTPlatform.Infrastructure.Protocol.AnSheng;
using IoTPlatform.Models;
using IoTPlatform.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace IoTPlatform.AnSheng.Tests;

/// <summary>
/// 安圣设备注册双路径收敛（T2/T3/T4/T5）回归测试。
///
/// 【基座】EF InMemory Provider + 直接 new 控制器 / 服务，不依赖 MySQL。
/// 【覆盖范围】
///   · <see cref="DeviceRegistrationGuard"/> 去重 / 存在性裁决（T2/T3 共用）。
///   · <see cref="DevicesController.CreateDevice"/> 租户 403、越权/冲突 409。
///   · <see cref="AnShengController.GetDiscoveredDevices"/> 超管不过滤 / 租户严格自见（T4）。
///   · <see cref="AnShengDiscoveryService.ClaimAsync"/> 非超管跨租户认领被拒（T3）。
/// </summary>
public sealed class DeviceClaimConsolidationTests
{
    // ─────────────────────────────────────────────────────────────
    // ① DeviceRegistrationGuard（T2/T3 共用校验）
    // ─────────────────────────────────────────────────────────────

    [Fact(DisplayName = "Guard① 待认领池已有同 IMEI（未认领）→ PendingPool，提示走认领")]
    public async Task Guard_PendingPool_When_UndiscoveredImeiExists()
    {
        await using var db = NewDb("guard-pending");
        db.Set<DiscoveredAnShengDevice>().Add(new DiscoveredAnShengDevice
        {
            Imei = "IMEI_IN_POOL",
            IsClaimed = false
        });
        await db.SaveChangesAsync();

        var r = await DeviceRegistrationGuard.CheckAsync(db, "IMEI_IN_POOL");

        Assert.Equal(DeviceRegistrationGuard.Conflict.PendingPool, r.Type);
        Assert.Contains("认领", r.Message);
    }

    [Fact(DisplayName = "Guard② devices 已有同 SerialNumber → ExistingDevice（409 冲突）")]
    public async Task Guard_ExistingDevice_When_SerialNumberExists()
    {
        await using var db = NewDb("guard-existing");
        db.Set<Device>().Add(new Device
        {
            Name = "已有设备",
            SerialNumber = "IMEI_DUP",
            Status = "offline",
            AppCode = "TENANT_A",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var r = await DeviceRegistrationGuard.CheckAsync(db, "IMEI_DUP");

        Assert.Equal(DeviceRegistrationGuard.Conflict.ExistingDevice, r.Type);
        Assert.Equal(1, r.ExistingDeviceId);
    }

    [Fact(DisplayName = "Guard③ 空 SerialNumber → 直接放行（无 IMEI 的手工设备）")]
    public async Task Guard_None_When_SerialNumberEmpty()
    {
        await using var db = NewDb("guard-empty");
        var r = await DeviceRegistrationGuard.CheckAsync(db, null);
        Assert.Equal(DeviceRegistrationGuard.Conflict.None, r.Type);

        var r2 = await DeviceRegistrationGuard.CheckAsync(db, "   ");
        Assert.Equal(DeviceRegistrationGuard.Conflict.None, r2.Type);
    }

    [Fact(DisplayName = "Guard④ 存在性闸门：有活跃 ANSHENG_MQTT 配置则放行；无则 NoOnlinePresence")]
    public async Task Guard_OnlinePresenceGate_DependsOnActiveConfig()
    {
        await using var db = NewDb("guard-presence");
        db.Set<ProtocolConfig>().Add(new ProtocolConfig
        {
            IsActive = true,
            ProtocolType = "ANSHENG_MQTT",
            AppCode = "TENANT_A"
        });
        await db.SaveChangesAsync();

        // 有活跃配置 + 提供了 IMEI + 无待认领/无设备 → 通过。
        var withConfig = DeviceRegistrationGuard.ApplyOnlinePresenceGate(
            await DeviceRegistrationGuard.CheckAsync(db, "IMEI_FRESH"));
        Assert.Equal(DeviceRegistrationGuard.Conflict.None, withConfig.Type);

        // 另一库：无任何活跃配置 + 提供了 IMEI → 凭空建孤儿设备，拒绝。
        await using var db2 = NewDb("guard-noconfig");
        var noConfig = DeviceRegistrationGuard.ApplyOnlinePresenceGate(
            await DeviceRegistrationGuard.CheckAsync(db2, "IMEI_ORPHAN"));
        Assert.Equal(DeviceRegistrationGuard.Conflict.NoOnlinePresence, noConfig.Type);
    }

    // ─────────────────────────────────────────────────────────────
    // ② DevicesController.CreateDevice（T2：租户 403 / 冲突 409）
    // ─────────────────────────────────────────────────────────────

    [Fact(DisplayName = "CreateDevice① 租户(admin) token 调 POST /devices → 403")]
    public async Task CreateDevice_Tenant_Returns_403()
    {
        var fake = new FakeDeviceService();
        var controller = NewDevicesController(fake, role: Roles.ADMIN);

        var action = await controller.CreateDevice(new CreateDeviceRequest { Name = "x" });

        var api = ExtractDeviceApi(action);
        Assert.Equal(403, api!.Code);
        Assert.False(fake.CreateCalled); // 绝不应落到服务层
    }

    [Fact(DisplayName = "CreateDevice② 同 SerialNumber 二次新增 → 409（共用 Guard）")]
    public async Task CreateDevice_DuplicateSerialNumber_Returns_409()
    {
        var fake = new ThrowingDeviceService(new DeviceRegistrationException(
            409, "设备 IMEI/SerialNumber IMEI_DUP 已存在（DeviceId=1），不可重复创建。",
            DeviceRegistrationGuard.Conflict.ExistingDevice));
        var controller = NewDevicesController(fake, role: Roles.SUPER_ADMIN);

        var action = await controller.CreateDevice(new CreateDeviceRequest
        {
            Name = "x",
            TargetAppCode = "TENANT_A",
            SerialNumber = "IMEI_DUP"
        });

        var api = ExtractDeviceApi(action);
        Assert.Equal(409, api!.Code);
    }

    [Fact(DisplayName = "CreateDevice③ 待认领池已有同 IMEI 时直接新增 → 409 提示走认领")]
    public async Task CreateDevice_PendingPoolImei_Returns_409_ClaimPrompt()
    {
        var fake = new ThrowingDeviceService(new DeviceRegistrationException(
            409, "设备 IMEI X 已在待认领池，请走认领流程。",
            DeviceRegistrationGuard.Conflict.PendingPool));
        var controller = NewDevicesController(fake, role: Roles.SUPER_ADMIN);

        var action = await controller.CreateDevice(new CreateDeviceRequest
        {
            Name = "x",
            TargetAppCode = "TENANT_A",
            SerialNumber = "X"
        });

        var api = ExtractDeviceApi(action);
        Assert.Equal(409, api!.Code);
        Assert.Contains("认领", api.Message);
    }

    [Fact(DisplayName = "CreateDevice④ 超级管理员指定目标 AppCode → 落到服务层（200 预校验通过）")]
    public async Task CreateDevice_SuperAdmin_PassesToService()
    {
        var fake = new FakeDeviceService();
        var controller = NewDevicesController(fake, role: Roles.SUPER_ADMIN);

        var action = await controller.CreateDevice(new CreateDeviceRequest
        {
            Name = "新设备",
            TargetAppCode = "TENANT_A"
        });

        var api = ExtractDeviceApi(action);
        Assert.Equal(200, api!.Code);
        Assert.True(fake.CreateCalled); // 已放行至服务层（落库正确性由 DeviceService 单测/集成覆盖）
    }

    // ─────────────────────────────────────────────────────────────
    // ③ AnShengController.GetDiscoveredDevices（T4：可见性）
    // ─────────────────────────────────────────────────────────────

    [Fact(DisplayName = "GetDiscovered① 超级管理员不过滤 → 看到全部（含历史 AppCode 行与 NULL 行）")]
    public async Task GetDiscovered_SuperAdmin_SeesAll()
    {
        var db = NewDb("disc-sa");
        SeedDiscovered(db, "IMEI_A", "TENANT_A");
        SeedDiscovered(db, "IMEI_B", "TENANT_B");
        SeedDiscovered(db, "IMEI_NULL", null);

        var controller = NewAnShengController(db, role: Roles.SUPER_ADMIN, appCode: null);

        var items = await QueryDiscoveredAsync(controller);
        Assert.Equal(3, items.Count);
    }

    [Fact(DisplayName = "GetDiscovered② 租户严格按自身 AppCode → 仅见本租户，NULL 行不算可见（消假空）")]
    public async Task GetDiscovered_Tenant_SeesOnlyOwn()
    {
        var db = NewDb("disc-tenant");
        SeedDiscovered(db, "IMEI_A", "TENANT_A");
        SeedDiscovered(db, "IMEI_B", "TENANT_B");
        SeedDiscovered(db, "IMEI_NULL", null);

        var controller = NewAnShengController(db, role: Roles.ADMIN, appCode: "TENANT_A");

        var items = await QueryDiscoveredAsync(controller);
        Assert.Single(items);
        Assert.Equal("IMEI_A", items[0].Imei); // AppCode 不在 DTO 暴露，IMEI 即可佐证「仅见本租户」
    }

    // ─────────────────────────────────────────────────────────────
    // ④ AnShengDiscoveryService.ClaimAsync（T3：跨租户认领被拒）
    // ─────────────────────────────────────────────────────────────

    [Fact(DisplayName = "Claim① 非超管认领归属其他租户的记录 → 403/跨租户被拒（CrossTenant）")]
    public async Task Claim_NonSuperAdmin_CrossTenant_Rejected()
    {
        var (service, provider) = NewDiscoveryService("claim-xtenant");
        using (var seedScope = provider.CreateScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Set<DiscoveredAnShengDevice>().Add(new DiscoveredAnShengDevice
            {
                Imei = "IMEI_X",
                AppCode = "TENANT_B", // 归属 B
                IsClaimed = false
            });
            db.SaveChanges();
        }

        var result = await service.ClaimAsync(new AnShengClaimCommand
        {
            Imei = "IMEI_X",
            AppCode = "TENANT_A", // 调用者是 A
            IsSuperAdmin = false,
            Kind = AnShengDeviceKind.Speaker4G
        });

        Assert.False(result.Success);
        Assert.Equal(AnShengClaimErrorCodes.CrossTenant, result.ErrorCode);
    }

    [Fact(DisplayName = "Claim② 超级管理员可认领任意租户记录（不受跨租户限制）")]
    public async Task Claim_SuperAdmin_CrossTenant_Allowed()
    {
        // 超级管理员走到「解析协议配置」阶段才会因无活跃配置而中止，
        // 但不会在跨租户检查处被拒 —— 证明跨租户拦截只对非超管生效。
        var (service, provider) = NewDiscoveryService("claim-sa");
        using (var seedScope = provider.CreateScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Set<DiscoveredAnShengDevice>().Add(new DiscoveredAnShengDevice
            {
                Imei = "IMEI_Y",
                AppCode = "TENANT_B",
                IsClaimed = false
            });
            db.SaveChanges();
        }

        var result = await service.ClaimAsync(new AnShengClaimCommand
        {
            Imei = "IMEI_Y",
            AppCode = "TENANT_A",
            IsSuperAdmin = true,
            Kind = AnShengDeviceKind.Speaker4G
        });

        // 无活跃 ANSHENG_MQTT 配置 → AdapterUnavailable，但不是 CrossTenant。
        Assert.False(result.Success);
        Assert.NotEqual(AnShengClaimErrorCodes.CrossTenant, result.ErrorCode);
        Assert.Equal(AnShengClaimErrorCodes.AdapterUnavailable, result.ErrorCode);
    }

    // ─────────────────────────────────────────────────────────────
    // 辅助
    // ─────────────────────────────────────────────────────────────

    private static AppDbContext NewDb(string name) =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(name)
            .EnableSensitiveDataLogging()
            .Options);

    private static DevicesController NewDevicesController(IDeviceService service, string role)
    {
        var controller = new DevicesController(service);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    new[] { new Claim(ClaimTypes.Role, role) }, "test"))
            }
        };
        return controller;
    }

    private static ApiResponse<DeviceDto>? ExtractDeviceApi(ActionResult<ApiResponse<DeviceDto>> action)
    {
        if (action.Value is ApiResponse<DeviceDto> v) return v;
        if (action.Result is OkObjectResult ok && ok.Value is ApiResponse<DeviceDto> rv) return rv;
        return null;
    }

    private static void SeedDiscovered(AppDbContext db, string imei, string? appCode)
    {
        db.Set<DiscoveredAnShengDevice>().Add(new DiscoveredAnShengDevice
        {
            Imei = imei,
            AppCode = appCode,
            IsClaimed = false
        });
        db.SaveChanges();
    }

    private static AnShengController NewAnShengController(AppDbContext db, string role, string? appCode)
    {
        var controller = new AnShengController(
            db, null!, null!, null!, null!, NullLogger<AnShengController>.Instance);

        var claims = new List<Claim> { new(ClaimTypes.Role, role) };
        if (appCode != null) claims.Add(new Claim("AppCode", appCode));

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"))
            }
        };
        return controller;
    }

    private static async Task<List<DiscoveredAnShengDeviceDto>> QueryDiscoveredAsync(AnShengController controller)
    {
        var action = await controller.GetDiscoveredDevices();
        var api = Assert.IsType<ApiResponse<DiscoveredDeviceListResponse>>(action.Value);
        Assert.Equal(200, api.Code);
        Assert.NotNull(api.Data);
        return api.Data.Items;
    }

    private static (AnShengDiscoveryService, ServiceProvider) NewDiscoveryService(string dbName)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .EnableSensitiveDataLogging()
            .Options;

        var services = new ServiceCollection();
        services.AddScoped(_ => new AppDbContext(options));
        services.AddScoped<IAnShengDeviceProfileService, FakeProfileService>();

        var provider = services.BuildServiceProvider();

        var service = new AnShengDiscoveryService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            new FakeProtocolAdapterFactory(),
            new UnusedProbeService(),
            NullLogger<AnShengDiscoveryService>.Instance);

        return (service, provider);
    }

    // ─────────────────────────────────────────────────────────────
    // 测试替身
    // ─────────────────────────────────────────────────────────────

    private sealed class FakeDeviceService : IDeviceService
    {
        public bool CreateCalled { get; private set; }

        public Task<PagedResponse<DeviceDto>> GetDevicesAsync(int page, int pageSize, string? keyword, string? status, string? appCode, List<long>? allowedAreaIds)
            => throw new NotImplementedException();
        public Task<DeviceDto?> GetDeviceAsync(long id, string? appCode, List<long>? allowedAreaIds)
            => throw new NotImplementedException();
        public Task<DeviceDto> CreateDeviceAsync(CreateDeviceRequest request)
        {
            CreateCalled = true;
            return Task.FromResult(new DeviceDto());
        }
        public Task<DeviceDto> UpdateDeviceAsync(long id, UpdateDeviceRequest request)
            => throw new NotImplementedException();
        public Task DeleteDeviceAsync(long id)
            => throw new NotImplementedException();
        public Task<List<DeviceDto>> GetDevicesByAreaAsync(long areaId, string? appCode, List<long>? allowedAreaIds)
            => throw new NotImplementedException();
        public Task<DeviceDetailDto?> GetDeviceDetailAsync(long id, string? appCode, List<long>? allowedAreaIds)
            => throw new NotImplementedException();
    }

    private sealed class ThrowingDeviceService : IDeviceService
    {
        private readonly Exception _ex;
        public ThrowingDeviceService(Exception ex) => _ex = ex;

        public Task<PagedResponse<DeviceDto>> GetDevicesAsync(int page, int pageSize, string? keyword, string? status, string? appCode, List<long>? allowedAreaIds)
            => throw new NotImplementedException();
        public Task<DeviceDto?> GetDeviceAsync(long id, string? appCode, List<long>? allowedAreaIds)
            => throw new NotImplementedException();
        public Task<DeviceDto> CreateDeviceAsync(CreateDeviceRequest request)
            => throw _ex;
        public Task<DeviceDto> UpdateDeviceAsync(long id, UpdateDeviceRequest request)
            => throw new NotImplementedException();
        public Task DeleteDeviceAsync(long id)
            => throw new NotImplementedException();
        public Task<List<DeviceDto>> GetDevicesByAreaAsync(long areaId, string? appCode, List<long>? allowedAreaIds)
            => throw new NotImplementedException();
        public Task<DeviceDetailDto?> GetDeviceDetailAsync(long id, string? appCode, List<long>? allowedAreaIds)
            => throw new NotImplementedException();
    }

    private sealed class FakeProtocolAdapterFactory : IProtocolAdapterFactory
    {
        public IProtocolAdapter CreateAdapter(string protocolType, int configId)
            => throw new NotSupportedException();
        public IProtocolAdapter? GetAdapter(int configId) => null;
        public void ReleaseAdapter(int configId) { }
        public void ReleaseAll() { }
    }

    private sealed class UnusedProbeService : IAnShengProbeService
    {
        public Task<AnShengProbeResult> ProbeAsync(int protocolConfigId, string imei, CancellationToken ct = default)
            => throw new NotSupportedException("认领跨租户检查不应触发探测");
        public void ClearPending()
            => throw new NotSupportedException();
    }

    private sealed class FakeProfileService : IAnShengDeviceProfileService
    {
        public Task<AnShengDeviceProfile?> GetByImeiAsync(string imei, CancellationToken cancellationToken = default)
            => Task.FromResult<AnShengDeviceProfile?>(null);
        public Task<AnShengDeviceProfile?> GetByDeviceIdAsync(long deviceId, CancellationToken cancellationToken = default)
            => Task.FromResult<AnShengDeviceProfile?>(null);
        public Task<AnShengDeviceProfile> GetOrCreateAsync(string imei, string appCode, CancellationToken cancellationToken = default)
            => Task.FromResult(new AnShengDeviceProfile());
        public (AnShengDeviceKind Kind, AnShengKindSource Source) ResolveKind(
            AnShengDeviceProfile? profile, AnShengCapabilitySnapshot snapshot, AnShengDeviceKind? manualKind = null)
            => (AnShengDeviceKind.Speaker4G, AnShengKindSource.Probe);
        public Task<AnShengDeviceProfile> ApplyProbeAsync(string imei, string appCode, AnShengCapabilitySnapshot snapshot, AnShengDeviceKind? manualKind = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new AnShengDeviceProfile());
        public Task<AnShengDeviceProfile> ApplyProbeFailureAsync(string imei, string appCode, string? error, CancellationToken cancellationToken = default)
            => Task.FromResult(new AnShengDeviceProfile());
        public Task<AnShengDeviceProfile?> RefreshAsync(string imei, string appCode, AnShengCapabilitySnapshot snapshot, CancellationToken cancellationToken = default)
            => Task.FromResult<AnShengDeviceProfile?>(null);
        public void AttachDevice(AnShengDeviceProfile profile, long deviceId) { }
    }
}
