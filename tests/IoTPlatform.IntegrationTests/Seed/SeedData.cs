using IoTPlatform.Data;
using IoTPlatform.Models;
using Microsoft.EntityFrameworkCore;

namespace IoTPlatform.IntegrationTests.Seed;

/// <summary>
/// 一次播种的产物。所有 Id 都是 MySQL 自增生成的<b>真实</b>主键，用例必须用它们而不是猜常量。
/// </summary>
/// <param name="CustomerId">租户主键。</param>
/// <param name="RoleId">管理员角色主键。</param>
/// <param name="UserId">管理员用户主键。</param>
/// <param name="ProtocolConfigId">安圣协议配置主键。</param>
/// <param name="DeviceId">主测试设备主键。</param>
/// <param name="Imei">主测试设备 IMEI（= <c>Device.SerialNumber</c>）。</param>
/// <param name="DiscoveredDeviceId">已发现设备记录主键。</param>
public sealed record SeedResult(
    long CustomerId,
    long RoleId,
    long UserId,
    long ProtocolConfigId,
    long DeviceId,
    string Imei,
    long DiscoveredDeviceId);

/// <summary>
/// 最小可用基线数据（架构方案 §3.7）。
///
/// 【最小的边界在哪】
///   只播种「让安圣下发链路 + 已发现设备列表跑通」所必需的 6 条记录：
///     Customer → Role → User → ProtocolConfig → Device → DiscoveredAnShengDevice
///   刻意不播种 Area/Project/Sensor/DataRecord —— 它们属于 T5–T14 的业务范围，
///   由具体用例按需追加，避免脚手架替业务做假设。
///
/// 【外键顺序】Device.ProtocolConfigId 依赖 ProtocolConfig.Id，
///   所以必须分两次 SaveChanges：先拿到自增 Id，再挂设备。
///
/// 【租户过滤】播种通过 DI 作用域直连 DbContext，此时 TenantContext.AppCode 为空，
///   <c>AppDbContext.ConfigureGlobalQueryFilters</c> 会跳过过滤器；且写入不受查询过滤器影响，
///   因此这里显式给每条记录都打上 <see cref="SharedTestConstants.AppCode"/>。
/// </summary>
public static class SeedData
{
    /// <summary>
    /// 向指定上下文播种基线数据。调用前应确保库已被 Respawn 清空。
    /// </summary>
    /// <param name="db">测试 schema 上的 <see cref="AppDbContext"/>。</param>
    /// <param name="imei">主设备 IMEI，默认 <see cref="SharedTestConstants.Imei"/>。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public static async Task<SeedResult> SeedAsync(
        AppDbContext db,
        string? imei = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(db);

        var deviceImei = string.IsNullOrWhiteSpace(imei) ? SharedTestConstants.Imei : imei;
        var now = DateTime.UtcNow;

        // ① 租户
        var customer = new Customer
        {
            Name = SharedTestConstants.CustomerName,
            Code = SharedTestConstants.CustomerCode,
            AppCode = SharedTestConstants.AppCode,
            ContactPerson = "集成测试",
            Status = "active",
            MaxDeviceCount = 1000,
            MaxUserCount = 100,
            MaxAreaCount = 100,
            CreatedAt = now,
            UpdatedAt = now
        };
        db.Customers.Add(customer);

        // ② 角色（权限交给 Roles.GetRolePermissions 兜底，此处只落库占位，不重复维护权限清单）
        var role = new Role
        {
            Code = SharedTestConstants.RoleAdmin,
            Name = "集成测试管理员",
            Description = "由 SeedData 播种，仅供集成测试使用",
            Permissions = "[]",
            DataScope = "ALL",
            AppCode = SharedTestConstants.AppCode,
            IsSystem = false,
            IsDefault = true,
            Status = "active",
            CreatedAt = now,
            UpdatedAt = now
        };
        db.Roles.Add(role);

        // ③ 协议配置：Type/ProtocolType 必须为 ANSHENG_MQTT，与 ProtocolAdapterFactory 的 switch 分支一致
        var protocolConfig = new ProtocolConfig
        {
            Name = "集成测试-安圣MQTT",
            Type = SharedTestConstants.ProtocolTypeAnSheng,
            ProtocolType = SharedTestConstants.ProtocolTypeAnSheng,
            Status = "active",
            IsActive = true,
            Description = "由 SeedData 播种，指向不可达回环 broker",
            AppCode = SharedTestConstants.AppCode,
            Config = """{"host":"127.0.0.1","port":1}""",
            CreatedAt = now,
            UpdatedAt = now
        };
        db.ProtocolConfigs.Add(protocolConfig);

        // 先落盘拿自增 Id，Device 才能挂外键
        await db.SaveChangesAsync(cancellationToken);

        // ④ 管理员用户（Email 有唯一索引，用固定值即可——每个用例前都会被 Respawn 清空）
        var user = new User
        {
            Username = "integration-admin",
            Password = "!seeded-not-a-real-hash!",
            Name = "集成测试管理员",
            FullName = "集成测试管理员",
            Email = "integration-admin@test.local",
            Status = "active",
            IsSuperAdmin = false,
            RoleId = role.Id,
            Role = SharedTestConstants.RoleAdmin,
            CustomerId = customer.Id,
            AppCode = SharedTestConstants.AppCode,
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now
        };
        db.Users.Add(user);

        // ⑤ 主测试设备：SerialNumber 必须等于 IMEI —— AnShengCommandService 用它推设备型号并拼下行主题
        var device = new Device
        {
            AppCode = SharedTestConstants.AppCode,
            Name = "集成测试-安圣设备",
            Model = "ANSHENG-TEST",
            SerialNumber = deviceImei,
            ProtocolConfigId = protocolConfig.Id,
            Category = "ansheng",
            Location = "集成测试机房",
            Status = "offline",
            MeterInstalled = false,
            CreatedAt = now,
            UpdatedAt = now
        };
        db.Devices.Add(device);

        // ⑥ 已发现设备：AppCode 打成 TEST，配合 AnShengController 的
        //    `d.AppCode == null || d.AppCode == appCode` 过滤，能被携带 X-Test-AppCode=TEST 的请求查到
        var discovered = new DiscoveredAnShengDevice
        {
            AppCode = SharedTestConstants.AppCode,
            Imei = deviceImei,
            Model = "ANSHENG-TEST",
            NetType = "4G",
            DiscoveredAt = now,
            LastSeenAt = now,
            IsClaimed = false
        };
        db.DiscoveredAnShengDevices.Add(discovered);

        await db.SaveChangesAsync(cancellationToken);

        // 播种用的实体不应残留在 ChangeTracker 中，否则后续查询会命中缓存而非真实库
        db.ChangeTracker.Clear();

        return new SeedResult(
            customer.Id,
            role.Id,
            user.Id,
            protocolConfig.Id,
            device.Id,
            deviceImei,
            discovered.Id);
    }

    /// <summary>
    /// 追加一台设备（多设备串扰类用例用）。复用已有的租户与协议配置。
    /// </summary>
    public static async Task<long> AddDeviceAsync(
        AppDbContext db,
        string imei,
        long protocolConfigId,
        string status = "offline",
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentException.ThrowIfNullOrWhiteSpace(imei);

        var now = DateTime.UtcNow;
        var device = new Device
        {
            AppCode = SharedTestConstants.AppCode,
            Name = $"集成测试-安圣设备-{imei}",
            Model = "ANSHENG-TEST",
            SerialNumber = imei,
            ProtocolConfigId = protocolConfigId,
            Category = "ansheng",
            Status = status,
            MeterInstalled = false,
            CreatedAt = now,
            UpdatedAt = now
        };

        db.Devices.Add(device);
        await db.SaveChangesAsync(cancellationToken);
        db.ChangeTracker.Clear();

        return device.Id;
    }
}
