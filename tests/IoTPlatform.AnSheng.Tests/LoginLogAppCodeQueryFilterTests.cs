using IoTPlatform.Data;
using IoTPlatform.Data.Repositories.Implementations;
using IoTPlatform.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace IoTPlatform.AnSheng.Tests;

/// <summary>
/// 验收「P0 根因修复」的另一半：<see cref="LogRepository.GetLoginLogsQuery"/> 必须按 AppCode 过滤，
/// 这是租户隔离（C 项）的底层机制。
///
/// 用 InMemory AppDbContext（_tenantContextAccessor 为 null ⇒ 无全局过滤器）直接驱动真实
/// <see cref="LogRepository"/>，断言其 AppCode 谓词语义：
///   · appCode="TENANT_A" 只返回该租户行，TENANT_B / null 行被排除；
///   · appCode=null（超管语义）返回全部，包括 null 行。
///
/// 之所以放到独立单测而非共享工厂集成：集成测试共享 TestWebAppFactory 的全局租户过滤器被「冻结」成
/// 单一 AppCode（见 SharedTestConstants 注释），无法在同一进程内构造两个真实租户。本单测绕过该限制，
/// 干净地验证仓库层的 AppCode 过滤逻辑本身。
/// </summary>
public sealed class LoginLogAppCodeQueryFilterTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly LogRepository _repo;

    public LoginLogAppCodeQueryFilterTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"loginlog-filter-{Guid.NewGuid():N}")
            .Options;
        _db = new AppDbContext(options);
        _repo = new LogRepository(_db);
    }

    public void Dispose()
    {
        _db.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact(DisplayName = "C-01 按 AppCode 查询只返回该租户行，排除其它租户与 null 行")]
    public async Task QueryByAppCode_ReturnsOnlyMatchingTenant()
    {
        // Arrange：三个租户/状态的登录日志
        SeedLog(appCode: "TENANT_A", email: "a@x.com");
        SeedLog(appCode: "TENANT_B", email: "b@x.com");
        SeedLog(appCode: null, email: "orphan@x.com"); // 历史未落 AppCode 的脏数据
        await _db.SaveChangesAsync();

        // Act
        var result = await _repo.GetLoginLogsQuery("TENANT_A").ToListAsync();

        // Assert：只命中 TENANT_A 一行
        Assert.Single(result);
        Assert.Equal("TENANT_A", result[0].AppCode);
        Assert.DoesNotContain(result, l => l.AppCode == "TENANT_B");
        Assert.DoesNotContain(result, l => l.AppCode == null);
    }

    [Fact(DisplayName = "C-02 不同租户互不串号：TENANT_B 查不到 TENANT_A 的行")]
    public async Task QueryByOtherAppCode_ReturnsOnlyThatTenant()
    {
        // Arrange
        SeedLog(appCode: "TENANT_A", email: "a@x.com");
        SeedLog(appCode: "TENANT_B", email: "b@x.com");
        await _db.SaveChangesAsync();

        // Act
        var result = await _repo.GetLoginLogsQuery("TENANT_B").ToListAsync();

        // Assert
        Assert.Single(result);
        Assert.Equal("TENANT_B", result[0].AppCode);
        Assert.DoesNotContain(result, l => l.AppCode == "TENANT_A");
    }

    [Fact(DisplayName = "C-03 appCode=null（超管语义）返回全部行，含 null 行")]
    public async Task QueryByNullAppCode_ReturnsAllRows()
    {
        // Arrange
        SeedLog(appCode: "TENANT_A", email: "a@x.com");
        SeedLog(appCode: "TENANT_B", email: "b@x.com");
        SeedLog(appCode: null, email: "orphan@x.com");
        await _db.SaveChangesAsync();

        // Act：超管的 AppCode claim 为空 ⇒ 不过滤
        var result = await _repo.GetLoginLogsQuery(appCode: null).ToListAsync();

        // Assert：全量可见（跨租户 + 历史 null 行）
        Assert.Equal(3, result.Count);
        Assert.Contains(result, l => l.AppCode == "TENANT_A");
        Assert.Contains(result, l => l.AppCode == "TENANT_B");
        Assert.Contains(result, l => l.AppCode == null);
    }

    private void SeedLog(string? appCode, string email)
    {
        _db.LoginLogs.Add(new LoginLog
        {
            UserId = 1,
            Email = email,
            UserName = "tester",
            Status = "success",
            AppCode = appCode,
            LoginTime = DateTime.UtcNow
        });
    }
}
