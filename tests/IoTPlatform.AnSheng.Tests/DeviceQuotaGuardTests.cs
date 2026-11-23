using IoTPlatform.Data;
using IoTPlatform.Models;
using IoTPlatform.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace IoTPlatform.AnSheng.Tests;

/// <summary>
/// 设备配额组件纯逻辑单测（不依赖真实 MySQL）。
///
/// 覆盖三态判定（不限 / 超标 / 配置缺失 fail-closed）、统一文案（真实 X/Y），
/// 以及最关键的「显式 Where(AppCode) 只数目标租户」——用跨租户样本证明不会数全表。
/// </summary>
public sealed class DeviceQuotaGuardTests
{
    /// <summary>
    /// 构造隔离的 InMemory 上下文。使用设计时构造函数（无 ITenantContextAccessor），
    /// 恰好模拟「超管 / AppCode 为空 → 全局过滤器不注册」的真实生产场景。
    /// </summary>
    private static AppDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new AppDbContext(options);
    }

    private static Customer NewCustomer(string appCode, int maxDeviceCount) => new()
    {
        Name = $"租户-{appCode}",
        Code = $"CODE-{appCode}",
        AppCode = appCode,
        Status = "active",
        MaxDeviceCount = maxDeviceCount
    };

    private static Device NewDevice(string appCode, string serialNumber) => new()
    {
        AppCode = appCode,
        Name = $"设备-{serialNumber}",
        SerialNumber = serialNumber,
        Status = "online"
    };

    [Fact(DisplayName = "Q-01 上限 0 = 不限量，且不得拒绝")]
    public void Snapshot_ZeroLimit_IsUnlimited_AndDoesNotReject()
    {
        var snapshot = new QuotaSnapshot(Current: 999, Limit: 0);

        Assert.True(snapshot.IsUnlimited);
        Assert.False(snapshot.Exceeded);
        Assert.False(snapshot.ShouldReject);
    }

    [Fact(DisplayName = "Q-02 上限 >0 的边界：差 1 放行、刚好到顶拒绝、存量超标拒绝")]
    public void Snapshot_PositiveLimit_Boundary_AndExistingOverLimit()
    {
        var belowLimit = new QuotaSnapshot(Current: 1, Limit: 2);
        var atLimit = new QuotaSnapshot(Current: 2, Limit: 2);
        var existingOverLimit = new QuotaSnapshot(Current: 3, Limit: 1); // 上限改小后存量 3 台

        Assert.False(belowLimit.Exceeded);
        Assert.False(belowLimit.ShouldReject);

        Assert.True(atLimit.Exceeded);
        Assert.True(atLimit.ShouldReject);

        Assert.True(existingOverLimit.Exceeded);
        Assert.True(existingOverLimit.ShouldReject);
    }

    [Fact(DisplayName = "Q-03 配置缺失 fail-closed：绝不被当作『0=不限』放行")]
    public void Snapshot_ConfigMissing_FailClosed()
    {
        var snapshot = new QuotaSnapshot(Current: 0, Limit: 0, ConfigMissing: true);

        Assert.True(snapshot.ConfigMissing);
        Assert.False(snapshot.IsUnlimited);
        Assert.False(snapshot.Exceeded);
        Assert.True(snapshot.ShouldReject);
    }

    [Fact(DisplayName = "Q-04 超限文案由唯一出口生成，X/Y 为真实数字")]
    public void BuildExceededMessage_ContainsRealNumbers()
    {
        var message = DeviceQuotaGuard.BuildExceededMessage(3, 1);

        Assert.Equal("已超出设备数量上限（当前 3/上限 1）", message);
        Assert.Equal(DeviceQuotaGuard.MESSAGE_TEMPLATE, "已超出设备数量上限（当前 {0}/上限 {1}）");
        Assert.Contains("3", message);
        Assert.Contains("1", message);
    }

    [Fact(DisplayName = "Q-05 ReadAsync 显式 Where(AppCode)：只数目标租户，不数全表")]
    public async Task ReadAsync_CountsOnlyTargetTenant_WithCrossTenantSamples()
    {
        using var db = CreateDb();
        db.Customers.AddRange(
            NewCustomer("tenant-a", 2),
            NewCustomer("tenant-b", 5));
        db.Devices.AddRange(
            NewDevice("tenant-a", "A-1"),
            NewDevice("tenant-a", "A-2"),
            NewDevice("tenant-b", "B-1"),
            NewDevice("tenant-b", "B-2"),
            NewDevice("tenant-b", "B-3"));
        await db.SaveChangesAsync();

        // 全表共 5 台；若裸 CountAsync() 会数成 5，下述断言即失败。
        Assert.Equal(5, await db.Devices.CountAsync());

        var a = await DeviceQuotaGuard.ReadAsync(db, "tenant-a");
        Assert.Equal(2, a.Current);
        Assert.Equal(2, a.Limit);
        Assert.True(a.Exceeded, "tenant-a 已满 2/2，应拒绝新增");

        var b = await DeviceQuotaGuard.ReadAsync(db, "tenant-b");
        Assert.Equal(3, b.Current);
        Assert.Equal(5, b.Limit);
        Assert.False(b.ShouldReject, "tenant-b 仅 3/5，应放行");
    }

    [Fact(DisplayName = "Q-06 目标租户无 Customer 行 → ConfigMissing=true（不放行）")]
    public async Task ReadAsync_MissingCustomer_ReturnsConfigMissing()
    {
        using var db = CreateDb();
        db.Devices.Add(NewDevice("orphan_tenant", "ORPHAN-1")); // 有设备、无租户配置
        await db.SaveChangesAsync();

        var snapshot = await DeviceQuotaGuard.ReadAsync(db, "orphan_tenant");

        Assert.True(snapshot.ConfigMissing);
        Assert.True(snapshot.ShouldReject);
    }

    [Fact(DisplayName = "Q-07 effectiveAppCode 为空 → ConfigMissing=true（不放行）")]
    public async Task ReadAsync_EmptyAppCode_ReturnsConfigMissing()
    {
        using var db = CreateDb();

        var snapshot = await DeviceQuotaGuard.ReadAsync(db, string.Empty);

        Assert.True(snapshot.ConfigMissing);
        Assert.True(snapshot.ShouldReject);
    }

    [Fact(DisplayName = "Q-08 上限 0：即使已有设备也不拦截")]
    public async Task ReadAsync_ZeroLimit_AllowsRegardlessOfCurrent()
    {
        using var db = CreateDb();
        db.Customers.Add(NewCustomer("unlimited", 0));
        db.Devices.AddRange(NewDevice("unlimited", "U-1"), NewDevice("unlimited", "U-2"));
        await db.SaveChangesAsync();

        var snapshot = await DeviceQuotaGuard.ReadAsync(db, "unlimited");

        Assert.False(snapshot.ConfigMissing);
        Assert.Equal(2, snapshot.Current);
        Assert.True(snapshot.IsUnlimited);
        Assert.False(snapshot.ShouldReject);
    }

    [Fact(DisplayName = "Q-09 上限改小不影响存量：快照只拒绝新增、不改任何 Device 行")]
    public async Task ReadAsync_LimitReduced_ExistingDevicesRemainUntouched()
    {
        using var db = CreateDb();
        db.Customers.Add(NewCustomer("shrink", 1));
        db.Devices.AddRange(
            NewDevice("shrink", "S-1"),
            NewDevice("shrink", "S-2"),
            NewDevice("shrink", "S-3"));
        await db.SaveChangesAsync();

        var snapshot = await DeviceQuotaGuard.ReadAsync(db, "shrink");

        Assert.True(snapshot.Exceeded);
        Assert.True(snapshot.ShouldReject);
        Assert.Equal("已超出设备数量上限（当前 3/上限 1）",
            DeviceQuotaGuard.BuildExceededMessage(snapshot.Current, snapshot.Limit));
        Assert.Equal(3, await db.Devices.CountAsync(d => d.AppCode == "shrink"));
    }
}
