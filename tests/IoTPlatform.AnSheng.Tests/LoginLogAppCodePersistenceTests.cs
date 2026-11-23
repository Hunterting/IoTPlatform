using IoTPlatform.Configuration;
using IoTPlatform.Data;
using IoTPlatform.Data.Repositories.Implementations;
using IoTPlatform.Data.Repositories.Interfaces;
using IoTPlatform.DTOs.Requests;
using IoTPlatform.Infrastructure.JWT;
using IoTPlatform.Models;
using IoTPlatform.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace IoTPlatform.AnSheng.Tests;

/// <summary>
/// 验收「P0 根因修复」：登录日志写入时必须落 <c>AppCode</c>，否则按 AppCode 查询永远 0 条。
///
/// 复用 AuthServiceTenantContextTests 的成熟套路（EF InMemory + 真实仓储）：
/// _tenantContextAccessor 为 null ⇒ AppDbContext 不挂全局租户过滤器，得到无过滤的干净视图，
/// 登录链路里调用的真实 <see cref="LogRepository.LogLoginAsync"/> 会直接写进 InMemory 的 login_logs，
/// 随后我们直接断言该表里的 AppCode 落库值。
///
/// 【覆盖】
///   · 下级租户用户（operator）登录 → login_logs 出现 1 行，AppCode == 该租户 AppCode（零回归）；
///   · admin 登录 → login_logs 出现 1 行但 AppCode 为 null（多客户模型 D7：admin 无单租户归属，
///     与超管同形，不伪造租户；原断言「AppCode=customer_001」已按新契约收口）；
///   · 超管登录 → login_logs 出现 1 行，AppCode 为 null。
///
/// 【无需 MySQL】：全程 InMemory，零外部依赖。
/// </summary>
public sealed class LoginLogAppCodePersistenceTests : IDisposable
{
    private const string RawPassword = "LoginLog@Test123";
    private static readonly string PasswordHash = BCrypt.Net.BCrypt.HashPassword(RawPassword);

    private readonly AppDbContext _db;
    private readonly AuthService _sut;

    public LoginLogAppCodePersistenceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"loginlog-persist-{Guid.NewGuid():N}")
            .Options;

        _db = new AppDbContext(options);

        _sut = new AuthService(
            userRepository: new UserRepository(_db),
            customerRepository: new CustomerRepository(_db),
            deviceRepository: new DeviceRepository(_db),
            logRepository: new LogRepository(_db),
            unitOfWork: null!,       // LoginAsync 不解引用
            jwtHelper: new JwtHelper(CreateJwtConfiguration()),
            cache: null!,            // 构造保存但被测方法不使用
            permissionService: new RoleConfigBackedPermissionService());
    }

    public void Dispose()
    {
        _db.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact(DisplayName = "B-01 admin 登录 → login_logs 落 1 行且 AppCode 为 null（D7 不伪造租户归属）")]
    public async Task TenantAdminLogin_WritesLoginLog_WithNullAppCode()
    {
        // Arrange：租户 admin 即使库里残留 AppCode=customer_001，也不作为登录日志的租户归属。
        SeedCustomer(1, "customer_001", "租户A");
        var user = SeedUser(11, "admin@tenant-a.com", Roles.ADMIN,
            isSuperAdmin: false, customerId: null, appCode: "customer_001");
        await CommitAsync();

        // Act
        await _sut.LoginAsync(new LoginRequest { Email = user.Email, Password = RawPassword });

        // Assert：admin 走集合分支，effectiveAppCode=null，与超管同形；日志仍按 userId 可追溯。
        var logs = _db.LoginLogs.ToList();
        Assert.Single(logs);
        Assert.Null(logs[0].AppCode);
        Assert.Equal("success", logs[0].Status);
        Assert.Equal(user.Id, logs[0].UserId);
    }

    [Fact(DisplayName = "B-02 超管登录 → login_logs 落 1 行且 AppCode 为 null")]
    public async Task SuperAdminLogin_WritesLoginLog_WithNullAppCode()
    {
        // Arrange：超管，IsSuperAdmin=true（AppCode 不被签发）
        SeedCustomer(1, "customer_001", "租户A");
        var user = SeedUser(1, "admin@system.com", Roles.SUPER_ADMIN,
            isSuperAdmin: true, customerId: null, appCode: "system");
        await CommitAsync();

        // Act
        await _sut.LoginAsync(new LoginRequest { Email = user.Email, Password = RawPassword });

        // Assert：超管登录日志 AppCode 必须为 null（修复后 ComputeEffectiveAppCode 对超管恒 null，
        // 这样超管在全局过滤器跳过时仍能看到全部登录日志）
        var logs = _db.LoginLogs.ToList();
        Assert.Single(logs);
        Assert.Null(logs[0].AppCode);
        Assert.Equal("success", logs[0].Status);
    }

    [Fact(DisplayName = "B-03 operator 角色登录同样落 AppCode（覆盖修复的另一种子角色）")]
    public async Task OperatorLogin_WritesLoginLog_WithTenantAppCode()
    {
        // Arrange：运维主管 operator，AppCode=customer_002
        SeedCustomer(2, "customer_002", "租户B");
        var user = SeedUser(21, "op@tenant-b.com", Roles.OPERATOR,
            isSuperAdmin: false, customerId: null, appCode: "customer_002");
        await CommitAsync();

        // Act
        await _sut.LoginAsync(new LoginRequest { Email = user.Email, Password = RawPassword });

        // Assert
        var logs = _db.LoginLogs.ToList();
        Assert.Single(logs);
        Assert.Equal("customer_002", logs[0].AppCode);
    }

    // ───────────────────────────── 夹具辅助 ─────────────────────────────

    private User SeedUser(long id, string email, string role, bool isSuperAdmin,
        long? customerId, string? appCode)
    {
        var now = new DateTime(2026, 1, 2, 9, 30, 0, DateTimeKind.Utc);
        var user = new User
        {
            Id = id,
            Username = $"user{id}",
            Password = PasswordHash,
            Name = $"用户{id}",
            FullName = $"用户{id}-全名",
            Email = email,
            Status = "active",
            Role = role,
            IsSuperAdmin = isSuperAdmin,
            CustomerId = customerId,
            AppCode = appCode,
            AllowedAreaIds = "1,2,3",
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now
        };
        _db.Users.Add(user);
        return user;
    }

    private void SeedCustomer(long id, string appCode, string name)
    {
        var now = new DateTime(2026, 1, 1, 8, 0, 0, DateTimeKind.Utc);
        _db.Customers.Add(new Customer
        {
            Id = id,
            Name = name,
            Code = $"C{id:000}",
            AppCode = appCode,
            ContactPerson = "测试联系人",
            ContactPhone = "13800000000",
            ContactEmail = "contact@test.local",
            Status = "active",
            CreatedAt = now,
            UpdatedAt = now
        });
    }

    private Task CommitAsync()
    {
        _db.SaveChanges();
        _db.ChangeTracker.Clear();
        return Task.CompletedTask;
    }

    private static IConfiguration CreateJwtConfiguration() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:SecretKey"] = "TEST_ONLY_FakeJwtSecret_NotForProduction_0123456789abcdef",
                ["Jwt:Issuer"] = "IoTPlatform",
                ["Jwt:Audience"] = "IoTPlatform",
                ["Jwt:ExpirationMinutes"] = "1440"
            })
            .Build();

    /// <summary>权限服务替身：按 role 回退到静态 RoleConfig，与生产语义一致；不依赖 MySQL。</summary>
    private sealed class RoleConfigBackedPermissionService : IPermissionService
    {
        public Task<List<string>> GetEffectivePermissionsAsync(string roleCode) =>
            Task.FromResult(Roles.IsSuperAdmin(roleCode)
                ? Permissions.All
                : Roles.GetRolePermissions(roleCode));

        public List<string> GetRolePermissions(string roleCode) => Roles.GetRolePermissions(roleCode);
        public Dictionary<string, List<string>> GetAllRolePermissions() => new();
        public List<string> GetAllPermissionCodes() => Permissions.All;
        public Dictionary<string, string> GetPermissionDescriptions() => new();
        public bool HasPermission(string roleCode, string permissionCode) =>
            GetRolePermissions(roleCode).Contains(permissionCode);
    }
}
