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
/// 双录入路径收敛（T2/T3/T4）的<b>边界</b>交叉验证。
///
/// 【基座】与 <see cref="DeviceClaimConsolidationTests"/> 一致的 EF InMemory + 直接 new 控制器/服务，不依赖 MySQL。
/// 【覆盖的边界】
///   · 在线校验门禁副作用（T2 caveat）：系统完全无活跃 ANSHENG_MQTT 配置时，超管直接新增
///     带 SerialNumber 的设备（含非安圣）是否被误 409；不带 SerialNumber 的设备是否仍放行。
///   · 超管认领「已被认领」的待认领记录（幂等/冲突处理，T3）——超管不豁免 AlreadyClaimed。
///   · 租户在请求体伪造 AppCode 直接新增（R3 越权兜底）——应 403 且请求体 AppCode 不被信任。
///   · 跨租户认领在超管维度确被放行（正向，含 AppCode 为 NULL 的待认领记录）。
/// </summary>
public sealed class DeviceClaimBoundaryTests
{
    // ─────────────────────────────────────────────────────────────
    // ① 在线校验门禁副作用（T2 工程师标注 caveat）
    // ─────────────────────────────────────────────────────────────

    [Fact(DisplayName = "门禁① 无活跃配置 + 带 SerialNumber 的设备 → NoOnlinePresence（409）")]
    public async Task Gate_NoActiveConfig_WithSerial_ReturnsNoOnlinePresence()
    {
        // 闸门逻辑只认「IMEI/SerialNumber 是否非空 + 是否存在活跃 ANSHENG_MQTT 配置」，
        // 不区分设备品类（安圣/非安圣）。因此只要请求带 SerialNumber、且系统全无活跃配置，
        // 无论安圣与否都会被判为「凭空建孤儿设备」而拒绝。
        await using var db = NewDb("gate-nocfg-serial");

        var r = DeviceRegistrationGuard.ApplyOnlinePresenceGate(
            await DeviceRegistrationGuard.CheckAsync(db, "NON_ANSHENG_SERIAL_001"));

        Assert.Equal(DeviceRegistrationGuard.Conflict.NoOnlinePresence, r.Type);
        Assert.Contains("ANSHENG_MQTT", r.Message);
    }

    [Fact(DisplayName = "门禁② 无活跃配置 + 不带 SerialNumber 的设备 → 放行（None）")]
    public async Task Gate_NoActiveConfig_EmptySerial_ReturnsNone()
    {
        // 唯一能绕过存在性闸门的路径：纯手工录入、不带任何 SerialNumber 的设备。
        // 这证明门禁的「放行口」是「无 IMEI」，而非「设备是非安圣品类」。
        await using var db = NewDb("gate-nocfg-noserial");

        var r = DeviceRegistrationGuard.ApplyOnlinePresenceGate(
            await DeviceRegistrationGuard.CheckAsync(db, null));

        Assert.Equal(DeviceRegistrationGuard.Conflict.None, r.Type);
    }

    [Fact(DisplayName = "门禁③ 有活跃配置 + 带 SerialNumber 的设备 → 放行（None，不误伤）")]
    public async Task Gate_WithActiveConfig_WithSerial_ReturnsNone()
    {
        await using var db = NewDb("gate-cfg-serial");
        db.Set<ProtocolConfig>().Add(new ProtocolConfig
        {
            IsActive = true,
            ProtocolType = "ANSHENG_MQTT",
            AppCode = "TENANT_A"
        });
        await db.SaveChangesAsync();

        var r = DeviceRegistrationGuard.ApplyOnlinePresenceGate(
            await DeviceRegistrationGuard.CheckAsync(db, "ANY_SERIAL_001"));

        Assert.Equal(DeviceRegistrationGuard.Conflict.None, r.Type);
    }

    // ─────────────────────────────────────────────────────────────
    // ② 超管认领「已被认领」的待认领记录（T3 幂等/冲突）
    // ─────────────────────────────────────────────────────────────

    [Fact(DisplayName = "认领① 超管认领已被认领记录 → AlreadyClaimed（超管不豁免）")]
    public async Task Claim_SuperAdmin_AlreadyClaimedRecord_ReturnsAlreadyClaimed()
    {
        var (service, provider) = NewDiscoveryService("claim-already");
        using (var seedScope = provider.CreateScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Set<DiscoveredAnShengDevice>().Add(new DiscoveredAnShengDevice
            {
                Imei = "IMEI_CLAIMED",
                AppCode = "TENANT_B",
                IsClaimed = true,
                ClaimedDeviceId = 99
            });
            db.SaveChanges();
        }

        var result = await service.ClaimAsync(new AnShengClaimCommand
        {
            Imei = "IMEI_CLAIMED",
            AppCode = "TENANT_A",
            IsSuperAdmin = true, // 即便超管，已认领也不应被重复认领
            Kind = AnShengDeviceKind.Speaker4G
        });

        Assert.False(result.Success);
        Assert.Equal(AnShengClaimErrorCodes.AlreadyClaimed, result.ErrorCode);
    }

    // ─────────────────────────────────────────────────────────────
    // ③ 租户伪造 AppCode 直接新增（R3 越权兜底，请求体 AppCode 不被信任）
    // ─────────────────────────────────────────────────────────────

    [Fact(DisplayName = "CreateDevice① 租户在请求体伪造 AppCode → 仍 403 且 AppCode 不被信任")]
    public async Task CreateDevice_Tenant_ForgedAppCode_Still403_AndAppCodeIgnored()
    {
        var fake = new FakeDeviceServiceB();
        // 租户（admin 角色）即便在请求体填入别人的 AppCode / TargetAppCode，也应被 403 拦截。
        var controller = NewDevicesController(fake, role: Roles.ADMIN);

        var action = await controller.CreateDevice(new CreateDeviceRequest
        {
            Name = "越权尝试",
            AppCode = "FORGED_OTHER_TENANT",      // 伪造归属租户
            TargetAppCode = "FORGED_OTHER_TENANT", // 伪造目标租户
            SerialNumber = "IMEI_FORGE"
        });

        var api = ExtractDeviceApi(action);
        Assert.Equal(403, api!.Code);
        Assert.False(fake.CreateCalled); // 绝不应落到服务层，伪造的 AppCode 未被信任
    }

    // ─────────────────────────────────────────────────────────────
    // ④ 跨租户认领在超管维度确被放行（正向，含 NULL AppCode 记录）
    // ─────────────────────────────────────────────────────────────

    [Fact(DisplayName = "认领② 超管认领 AppCode=NULL 的待认领记录 → 越过跨租户门禁（直达 AdapterUnavailable）")]
    public async Task Claim_SuperAdmin_NullAppCodeRecord_PassesCrossTenantGate()
    {
        var (service, provider) = NewDiscoveryService("claim-nullapp");
        using (var seedScope = provider.CreateScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<AppDbContext>();
            // 配额 fail-closed：先为目标租户落一条 Customer 配置（0=不限），
            // 否则新守卫会在探测前以 DEVICE_QUOTA_CONFIG_MISSING 拒绝，走不到本用例要验的跨租户门禁。
            db.Customers.Add(new Customer
            {
                Name = "租户A",
                Code = "TENANT_A",
                AppCode = "TENANT_A",
                Status = "active",
                MaxDeviceCount = 0
            });
            db.Set<DiscoveredAnShengDevice>().Add(new DiscoveredAnShengDevice
            {
                Imei = "IMEI_NULLAPP",
                AppCode = null, // 未归属任何租户
                IsClaimed = false
            });
            db.SaveChanges();
        }

        var result = await service.ClaimAsync(new AnShengClaimCommand
        {
            Imei = "IMEI_NULLAPP",
            AppCode = "TENANT_A",
            IsSuperAdmin = true,
            Kind = AnShengDeviceKind.Speaker4G
        });

        // 跨租户拦截要求 discovered.AppCode 非空，NULL 记录天然可认领；
        // 无活跃配置时停在 AdapterUnavailable，而非 CrossTenant。
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
        var controller = new DevicesController(service, null, null);
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

    private static (AnShengDiscoveryService, ServiceProvider) NewDiscoveryService(string dbName)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .EnableSensitiveDataLogging()
            .Options;

        var services = new ServiceCollection();
        services.AddScoped(_ => new AppDbContext(options));
        services.AddScoped<IAnShengDeviceProfileService, FakeProfileServiceB>();

        var provider = services.BuildServiceProvider();

        var service = new AnShengDiscoveryService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            new FakeProtocolAdapterFactoryB(),
            new UnusedProbeServiceB(),
            NullLogger<AnShengDiscoveryService>.Instance);

        return (service, provider);
    }

    // ─────────────────────────────────────────────────────────────
    // 测试替身（与既有套件同名类型不冲突：均为本类的嵌套 private）
    // ─────────────────────────────────────────────────────────────

    private sealed class FakeDeviceServiceB : IDeviceService
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

    private sealed class FakeProtocolAdapterFactoryB : IProtocolAdapterFactory
    {
        public IProtocolAdapter CreateAdapter(string protocolType, int configId)
            => throw new NotSupportedException();
        public IProtocolAdapter? GetAdapter(int configId) => null;
        public void ReleaseAdapter(int configId) { }
        public void ReleaseAll() { }
    }

    private sealed class UnusedProbeServiceB : IAnShengProbeService
    {
        public Task<AnShengProbeResult> ProbeAsync(int protocolConfigId, string imei, CancellationToken ct = default)
            => throw new NotSupportedException("认领跨租户检查不应触发探测");
        public void ClearPending()
            => throw new NotSupportedException();
    }

    private sealed class FakeProfileServiceB : IAnShengDeviceProfileService
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
