using System.IdentityModel.Tokens.Jwt;
using System.Linq.Expressions;
using System.Security.Claims;
using IoTPlatform.Configuration;
using IoTPlatform.Data;
using IoTPlatform.Data.Repositories.Implementations;
using IoTPlatform.Data.Repositories.Interfaces;
using IoTPlatform.DTOs.Requests;
using IoTPlatform.DTOs.Responses;
using IoTPlatform.Infrastructure.JWT;
using IoTPlatform.Models;
using IoTPlatform.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace IoTPlatform.AnSheng.Tests;

/// <summary>
/// 「客户管理页租户看不到数据」修复 —— <see cref="AuthService"/> 登录链路租户字段回填的针对性单测。
///
/// 对应 docs/customer-tenant-context/solution.md §5（存量数据兼容 S1/S2/S3/S4/S5）与 §6.4（自动化用例）。
///
/// 【为什么用 EF InMemory + 真实仓储】
///   本轮修复的关键之一是复用已存在的 ICustomerRepository.GetByAppCodeAsync 做存量库反查，
///   因此 UserRepository / CustomerRepository / DeviceRepository / LogRepository 全部用真实实现，
///   只把 AppDbContext 换成 InMemory（沿用 AnShengDeviceProfileServiceTests 的"设计时重载"写法：
///   _tenantContextAccessor 为 null 时不挂全局租户过滤器，得到无过滤的干净视图）。
///   这样 GetByAppCodeAsync 的 FirstOrDefaultAsync 语义也被真实覆盖，而不是被替身猜出来。
///
/// 【数据形态】每个用例一套独立内存库，构造三种形态：
///   S1 存量库 User.CustomerId=NULL + AppCode=customer_001（Staging 现状）
///   S2 新库   User.CustomerId 指向 Customer
///   S3 超管   IsSuperAdmin=true（零回归）
///   S4 兜底   AppCode 在 customers 表无对应行 / CustomerId 悬空
///
/// 【多客户授权模型 T03 收口】role=admin 的账号不再走单值回填：CustomerId / AppCode /
/// CustomerName / CurrentCustomer 恒为 null，ManagedCustomerIds 来自 user_customers（DB 为准），
/// JWT 不签发单值 claim。下级（user/operator）与超管链路逐字不变（零回归）。
/// 因此 S1/S2/S4-01 中原本断言 admin 单值回填的用例已按新契约改写。
/// </summary>
public sealed class AuthServiceTenantContextTests : IDisposable
{
    private const string RawPassword = "TenantCtx@123";

    // BCrypt 代价因子较高，整个类只算一次，避免每个用例重复付 100ms+
    private static readonly string PasswordHash = BCrypt.Net.BCrypt.HashPassword(RawPassword);

    private readonly AppDbContext _db;
    private readonly SpyCustomerRepository _customers;
    private readonly RecordingPermissionService _permissions;
    private readonly AuthService _sut;

    public AuthServiceTenantContextTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"tenantctx-{Guid.NewGuid():N}")
            .Options;

        _db = new AppDbContext(options);
        _customers = new SpyCustomerRepository(new CustomerRepository(_db));
        _permissions = new RecordingPermissionService();

        _sut = new AuthService(
            userRepository: new UserRepository(_db),
            customerRepository: _customers,
            deviceRepository: new DeviceRepository(_db),
            logRepository: new LogRepository(_db),
            unitOfWork: null!,       // LoginAsync / GetCurrentUserAsync / SwitchCustomerAsync 三条链路均未解引用
            jwtHelper: new JwtHelper(CreateJwtConfiguration()),
            cache: null!,            // 同上；两个依赖仅在构造时保存，不在被测方法中使用
            permissionService: _permissions,
            // admin 分支从 user_customers 读集合（DB 为准），与生产 DI 装配一致。
            customerScopeService: new CustomerScopeService(
                new UserCustomerRepository(_db), _customers));
    }

    public void Dispose()
    {
        _db.Dispose();
        GC.SuppressFinalize(this);
    }

    // ─────────────────────────────────────────────────────────────
    // 一、S1 存量库形态（CustomerId 全 NULL，AppCode 为身份主源）
    // ─────────────────────────────────────────────────────────────

    [Fact(DisplayName = "TC-S1-01 admin 存量库登录 → 单值全 null、集合来自 user_customers、CurrentCustomer=null")]
    public async Task S1_LegacyDb_Login_AdminUsesManagedSet()
    {
        // Arrange
        var customer = SeedCustomer(1, "customer_001", "海底捞智慧工厂有限公司");
        var user = SeedUser(11, "manager@haidilao.com", Roles.ADMIN,
            isSuperAdmin: false, customerId: null, appCode: "customer_001");
        SeedGrant(user.Id, customer.Id);
        await CommitAsync();

        // Act
        var response = await LoginAsync(user.Email);

        // Assert：D7 admin 即使库里残留旧单值也不采信，集合由授权表决定。
        Assert.Null(response.User.AppCode);
        Assert.Null(response.User.CustomerId);
        Assert.Null(response.User.CustomerName);
        Assert.Null(response.CurrentCustomer);
        Assert.NotNull(response.User.ManagedCustomerIds);
        Assert.Equal(new[] { customer.Id }, response.User.ManagedCustomerIds);

        // 纯追加字段也应被补齐（§3.1.1 改动 2）
        Assert.Equal(user.FullName, response.User.Name);
        Assert.True(response.User.IsActive);
        Assert.Equal(user.CreatedAt, response.User.CreatedAt);
        Assert.Equal(user.UpdatedAt, response.User.UpdatedAt);

        // 权限计算链路未被破坏（三处 GetEffectivePermissionsAsync 保留）
        Assert.NotEmpty(response.User.Permissions);
        Assert.Equal(new[] { Roles.ADMIN }, _permissions.RolesAsked);
        Assert.Equal(Roles.GetAdminPermissions(), response.User.Permissions);
    }

    [Fact(DisplayName = "TC-S1-02 admin 登录 → 不反查单客户；JWT 签集合快照且不签单值 claim")]
    public async Task S1_LegacyDb_AdminLogin_JwtCarriesOnlyManagedSet()
    {
        // Arrange
        SeedCustomer(1, "customer_001", "海底捞");
        var user = SeedUser(11, "manager@haidilao.com", Roles.ADMIN,
            isSuperAdmin: false, customerId: null, appCode: "customer_001");
        SeedGrant(user.Id, 1);
        await CommitAsync();

        // Act
        var response = await LoginAsync(user.Email);

        // Assert：admin 走集合分支，CustomerId / AppCode 两条反查路径都不走。
        Assert.Empty(_customers.IdQueries);
        Assert.Empty(_customers.AppCodeQueries);

        var token = DecodeToken(response.Token);
        Assert.Null(GetClaim(token, "AppCode"));
        Assert.Null(GetClaim(token, "CustomerId"));
        Assert.Equal("1", GetClaim(token, CustomerScopeConstants.ClaimManagedCustomerIds));
        Assert.Equal(Roles.ADMIN, GetClaim(token, ClaimTypes.Role));
    }

    [Fact(DisplayName = "TC-S1-03 admin /auth/me → 与登录响应同形（单值 null、集合一致、permissions 保留）")]
    public async Task S1_LegacyDb_GetCurrentUser_AdminMatchesLogin()
    {
        // Arrange
        var customer = SeedCustomer(1, "customer_001", "海底捞");
        var user = SeedUser(11, "manager@haidilao.com", Roles.ADMIN,
            isSuperAdmin: false, customerId: null, appCode: "customer_001");
        SeedGrant(user.Id, customer.Id);
        await CommitAsync();
        var login = await LoginAsync(user.Email);
        _customers.AppCodeQueries.Clear(); // 隔离本次 /auth/me 的调用记录

        // Act
        var me = await _sut.GetCurrentUserAsync(user.Id);

        // Assert
        Assert.NotNull(me);
        Assert.Null(me!.User.AppCode);
        Assert.Null(me.User.CustomerId);
        Assert.Null(me.User.CustomerName);
        Assert.Null(me.CurrentCustomer);
        Assert.Equal(login.User.ManagedCustomerIds, me.User.ManagedCustomerIds);
        Assert.Equal(new[] { customer.Id }, me.User.ManagedCustomerIds);
        Assert.Equal(login.User.Permissions, me.User.Permissions);
        Assert.Empty(_customers.AppCodeQueries);
        Assert.Equal(new[] { Roles.ADMIN, Roles.ADMIN }, _permissions.RolesAsked);
    }

    // ─────────────────────────────────────────────────────────────
    // 二、S2 新库形态（CustomerId 有值，快速路径）
    // ─────────────────────────────────────────────────────────────

    [Fact(DisplayName = "TC-S2-01 admin 新库登录 → 旧单值被忽略，集合来自授权表且零反查")]
    public async Task S2_NewDb_AdminLogin_IgnoresLegacySingleValues()
    {
        // Arrange
        var customer = SeedCustomer(1, "customer_001", "海底捞");
        var user = SeedUser(11, "manager@haidilao.com", Roles.ADMIN,
            isSuperAdmin: false, customerId: customer.Id, appCode: "customer_001");
        SeedGrant(user.Id, customer.Id);
        await CommitAsync();

        // Act
        var response = await LoginAsync(user.Email);

        // Assert：防御性收口——即使库里残留 CustomerId/AppCode 也不采信。
        Assert.Null(response.User.AppCode);
        Assert.Null(response.User.CustomerId);
        Assert.Null(response.User.CustomerName);
        Assert.Null(response.CurrentCustomer);
        Assert.Equal(new[] { customer.Id }, response.User.ManagedCustomerIds);
        Assert.Empty(_customers.IdQueries);
        Assert.Empty(_customers.AppCodeQueries);
        Assert.NotEmpty(response.User.Permissions);
    }

    [Fact(DisplayName = "TC-S2-02 admin 新库 /auth/me → 导航属性不被采信，字段与登录响应一致")]
    public async Task S2_NewDb_GetCurrentUser_AdminMatchesLogin()
    {
        // Arrange
        var customer = SeedCustomer(1, "customer_001", "海底捞");
        var user = SeedUser(11, "manager@haidilao.com", Roles.ADMIN,
            isSuperAdmin: false, customerId: customer.Id, appCode: "customer_001");
        SeedGrant(user.Id, customer.Id);
        await CommitAsync();
        var login = await LoginAsync(user.Email);

        // Act
        var me = await _sut.GetCurrentUserAsync(user.Id);

        // Assert
        Assert.NotNull(me);
        Assert.Null(me!.User.AppCode);
        Assert.Null(me.User.CustomerId);
        Assert.Null(me.User.CustomerName);
        Assert.Null(me.CurrentCustomer);
        Assert.Equal(login.User.ManagedCustomerIds, me.User.ManagedCustomerIds);
        Assert.Equal(login.User.Permissions, me.User.Permissions);
        Assert.Empty(_customers.AppCodeQueries);
    }

    // ─────────────────────────────────────────────────────────────
    // 三、S3 超管零回归
    // ─────────────────────────────────────────────────────────────

    [Fact(DisplayName = "TC-S3-01 超管登录 → appCode/customerId/customerName/currentCustomer 全 null，权限全量")]
    public async Task S3_SuperAdmin_Login_KeepsAllNullInvariants()
    {
        // Arrange：库里有 3 个租户，超管 AppCode=system（若被签发会把自己过滤成 0 条）
        SeedCustomer(1, "customer_001", "租户A");
        SeedCustomer(2, "customer_002", "租户B");
        SeedCustomer(3, "customer_003", "租户C");
        var user = SeedUser(1, "admin@system.com", Roles.SUPER_ADMIN,
            isSuperAdmin: true, customerId: null, appCode: "system");
        await CommitAsync();

        // Act
        var response = await LoginAsync(user.Email);

        // Assert：P-3 超管不变量逐字不变
        Assert.Null(response.User.AppCode);
        Assert.Null(response.User.CustomerId);
        Assert.Null(response.User.CustomerName);
        Assert.Null(response.CurrentCustomer);
        Assert.True(response.User.IsSuperAdmin);

        // 超管短路：两步都不查
        Assert.Empty(_customers.IdQueries);
        Assert.Empty(_customers.AppCodeQueries);

        // JWT 不签发 AppCode claim，role claim 不变
        var token = DecodeToken(response.Token);
        Assert.Null(GetClaim(token, "AppCode"));
        Assert.Equal(Roles.SUPER_ADMIN, GetClaim(token, ClaimTypes.Role));

        // 权限仍按 role 解析且非空
        Assert.NotEmpty(response.User.Permissions);
        Assert.Equal(Permissions.All, response.User.Permissions);
        Assert.Equal(new[] { Roles.SUPER_ADMIN }, _permissions.RolesAsked);
    }

    [Fact(DisplayName = "TC-S3-02 超管 /auth/me → 与登录响应同样保持全 null")]
    public async Task S3_SuperAdmin_GetCurrentUser_KeepsAllNullInvariants()
    {
        // Arrange
        SeedCustomer(1, "customer_001", "租户A");
        var user = SeedUser(1, "admin@system.com", Roles.SUPER_ADMIN,
            isSuperAdmin: true, customerId: null, appCode: "system");
        await CommitAsync();

        // Act
        var me = await _sut.GetCurrentUserAsync(user.Id);

        // Assert
        Assert.NotNull(me);
        Assert.Null(me!.User.AppCode);
        Assert.Null(me.User.CustomerId);
        Assert.Null(me.User.CustomerName);
        Assert.Null(me.CurrentCustomer);
        Assert.NotEmpty(me.User.Permissions);
        Assert.Empty(_customers.AppCodeQueries);
        Assert.Equal(new[] { Roles.SUPER_ADMIN }, _permissions.RolesAsked);
    }

    // ─────────────────────────────────────────────────────────────
    // 四、S4 兜底与边界（§5 S4 / S5）
    // ─────────────────────────────────────────────────────────────

    [Fact(DisplayName = "TC-S4-01 admin 残留无效 AppCode → 不抛异常，单值仍全 null、集合按授权表回填")]
    public async Task S4_AdminWithUnmatchedLegacyAppCode_FallsBackToManagedSet()
    {
        // Arrange
        SeedCustomer(1, "customer_001", "租户A"); // 与用户 AppCode 不匹配
        var user = SeedUser(12, "orphan@tenant.local", Roles.ADMIN,
            isSuperAdmin: false, customerId: null, appCode: "customer_999");
        SeedGrant(user.Id, 1);
        await CommitAsync();

        // Act
        var response = await LoginAsync(user.Email);

        // Assert：admin 不再反查无效 AppCode，也不伪造租户身份。
        Assert.Null(response.User.AppCode);
        Assert.Null(response.User.CustomerId);
        Assert.Null(response.User.CustomerName);
        Assert.Null(response.CurrentCustomer);
        Assert.Equal(new[] { 1L }, response.User.ManagedCustomerIds);
        Assert.Empty(_customers.AppCodeQueries);
        Assert.Null(GetClaim(DecodeToken(response.Token), "AppCode"));
        Assert.NotEmpty(response.User.Permissions);
    }

    [Fact(DisplayName = "TC-S4-02 CustomerId 悬空指向不存在客户 → 回落 AppCode 反查成功")]
    public async Task S4_DanglingCustomerId_FallsBackToAppCodeLookup()
    {
        // Arrange：§5 表 S4 边界
        var customer = SeedCustomer(2, "customer_002", "绿能科技");
        var user = SeedUser(13, "user@greenenergy.com", Roles.USER,
            isSuperAdmin: false, customerId: 999, appCode: "customer_002");
        await CommitAsync();

        // Act
        var response = await LoginAsync(user.Email);

        // Assert
        Assert.Equal("customer_002", response.User.AppCode);
        Assert.Equal<long?>(customer.Id, response.User.CustomerId);
        Assert.Equal(customer.Name, response.User.CustomerName);
        Assert.NotNull(response.CurrentCustomer);
        Assert.Equal("customer_002", response.CurrentCustomer!.AppCode);
        Assert.Equal(new[] { 999L }, _customers.IdQueries);
        Assert.Equal(new[] { "customer_002" }, _customers.AppCodeQueries);
        Assert.NotEmpty(response.User.Permissions);
    }

    // ─────────────────────────────────────────────────────────────
    // 五、权限链路与 SwitchCustomerAsync（仅 API 可达的第三处 new UserDto）
    // ─────────────────────────────────────────────────────────────

    [Fact(DisplayName = "TC-PERM-01 三条登录链路均调用 GetEffectivePermissionsAsync(user.Role) 且结果非空")]
    public async Task Permissions_AllThreePaths_CallEffectivePermissionsWithUserRole()
    {
        // Arrange
        SeedCustomer(2, "customer_002", "绿能科技");
        var tenant = SeedUser(13, "user@greenenergy.com", Roles.USER,
            isSuperAdmin: false, customerId: null, appCode: "customer_002");
        var admin = SeedUser(1, "admin@system.com", Roles.SUPER_ADMIN,
            isSuperAdmin: true, customerId: null, appCode: "system");
        await CommitAsync();

        // Act
        var login = await LoginAsync(tenant.Email);
        var me = await _sut.GetCurrentUserAsync(tenant.Id);
        var switched = await _sut.SwitchCustomerAsync(admin.Id, 2);

        // Assert：三处调用入参严格是 user.Role，且权限集合与改动前静态快照一致
        Assert.Equal(new[] { Roles.USER, Roles.USER, Roles.SUPER_ADMIN }, _permissions.RolesAsked);
        Assert.Equal(Roles.GetUserPermissions(), login.User.Permissions);
        Assert.Equal(Roles.GetUserPermissions(), me!.User.Permissions);
        Assert.Equal(Permissions.All, switched!.User.Permissions);
        Assert.All(new[] { login.User.Permissions, me.User.Permissions, switched.User.Permissions },
            perms => Assert.NotEmpty(perms));
    }

    [Fact(DisplayName = "TC-SW-01 超管切换客户 → DTO 的 appCode/customerId 与新签发 JWT 严格对齐")]
    public async Task SwitchCustomer_DtoFieldsAlignedWithNewJwt()
    {
        // Arrange
        var customer = SeedCustomer(2, "customer_002", "绿能科技");
        var admin = SeedUser(1, "admin@system.com", Roles.SUPER_ADMIN,
            isSuperAdmin: true, customerId: null, appCode: "system");
        await CommitAsync();

        // Act
        var response = await _sut.SwitchCustomerAsync(admin.Id, customer.Id);

        // Assert：「以租户视角查看」的显式例外（§4.4）
        Assert.NotNull(response);
        Assert.Equal(customer.AppCode, response!.User.AppCode);
        Assert.Equal<long?>(customer.Id, response.User.CustomerId);
        Assert.Equal(customer.Name, response.User.CustomerName);
        Assert.NotNull(response.CurrentCustomer);
        Assert.Equal(customer.AppCode, response.CurrentCustomer!.AppCode);
        Assert.Null(response.CurrentCustomer.ContactEmail); // includeContactEmail:false 零回归

        var token = DecodeToken(response.Token);
        Assert.Equal(customer.AppCode, GetClaim(token, "AppCode"));
        Assert.Equal(customer.Id.ToString(), GetClaim(token, "CustomerId"));
        Assert.Equal(Roles.SUPER_ADMIN, GetClaim(token, ClaimTypes.Role));
        Assert.Equal(Permissions.All, response.User.Permissions);
    }

    [Fact(DisplayName = "TC-SW-02 非超管调用切换客户 → 拒绝（权限链路零回归）")]
    public async Task SwitchCustomer_NonSuperAdmin_Throws()
    {
        // Arrange
        SeedCustomer(2, "customer_002", "绿能科技");
        var tenant = SeedUser(13, "user@greenenergy.com", Roles.USER,
            isSuperAdmin: false, customerId: null, appCode: "customer_002");
        await CommitAsync();

        // Act / Assert
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _sut.SwitchCustomerAsync(tenant.Id, 2));
    }

    [Fact(DisplayName = "TC-EDGE-01 密码错误 → UnauthorizedAccessException（登录主流程零回归）")]
    public async Task Login_WrongPassword_ThrowsUnauthorized()
    {
        // Arrange
        SeedCustomer(1, "customer_001", "租户A");
        var user = SeedUser(11, "manager@haidilao.com", Roles.ADMIN,
            isSuperAdmin: false, customerId: null, appCode: "customer_001");
        await CommitAsync();

        // Act / Assert
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _sut.LoginAsync(new LoginRequest { Email = user.Email, Password = "wrong-password" }));
        Assert.Empty(_permissions.RolesAsked); // 失败路径不应触发权限计算
    }

    [Fact(DisplayName = "TC-EDGE-02 /auth/me 用户不存在 → 返回 null")]
    public async Task GetCurrentUser_UnknownId_ReturnsNull()
    {
        // Act
        var me = await _sut.GetCurrentUserAsync(987654);

        // Assert
        Assert.Null(me);
    }

    // ─────────────────────────────────────────────────────────────
    // 测试夹具辅助
    // ─────────────────────────────────────────────────────────────

    private Task<LoginResponse> LoginAsync(string email) =>
        _sut.LoginAsync(new LoginRequest { Email = email, Password = RawPassword });

    private Customer SeedCustomer(long id, string appCode, string name)
    {
        var now = new DateTime(2026, 1, 1, 8, 0, 0, DateTimeKind.Utc);
        var customer = new Customer
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
        };
        _db.Customers.Add(customer);
        return customer;
    }

    /// <summary>为 admin 写入 user_customers 授权行（DB 权威），模拟生产 seed / 迁移回填结果。</summary>
    private void SeedGrant(long userId, long customerId) => _db.UserCustomers.Add(new UserCustomer
    {
        UserId = userId,
        CustomerId = customerId,
        GrantedByUserId = null,
        Source = CustomerScopeConstants.SourceSeed,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow,
        RevokedAt = null
    });

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

    /// <summary>落盘并清空 ChangeTracker，模拟"新请求"重新从库加载（含 Include 导航）。</summary>
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
                // 测试专用假密钥：本测试仅断言 JWT claim（ReadJwtToken 只解码、不验签），
                // 密钥值与生产配置解耦。切勿从 appsettings 复制真实 SecretKey 到测试代码。
                ["Jwt:SecretKey"] = "TEST_ONLY_FakeJwtSecret_NotForProduction_0123456789abcdef",
                ["Jwt:Issuer"] = "IoTPlatform",
                ["Jwt:Audience"] = "IoTPlatform",
                ["Jwt:ExpirationMinutes"] = "1440"
            })
            .Build();

    private static JwtSecurityToken DecodeToken(string token) =>
        new JwtSecurityTokenHandler().ReadJwtToken(token);

    private static string? GetClaim(JwtSecurityToken token, string type) =>
        token.Claims.FirstOrDefault(c => c.Type == type)?.Value;

    /// <summary>
    /// 包装真实 CustomerRepository 的探针：记录 GetByIdAsync / GetByAppCodeAsync 的调用，
    /// 用于断言「仅需要时才反查」（solution.md §6.4）。其余成员原样委托。
    /// </summary>
    private sealed class SpyCustomerRepository : ICustomerRepository
    {
        private readonly ICustomerRepository _inner;

        public SpyCustomerRepository(ICustomerRepository inner) => _inner = inner;

        public List<long> IdQueries { get; } = new();
        public List<string> AppCodeQueries { get; } = new();

        public Task<Customer?> GetByAppCodeAsync(string appCode)
        {
            AppCodeQueries.Add(appCode);
            return _inner.GetByAppCodeAsync(appCode);
        }

        public Task<Customer?> GetByIdAsync(long id, string[]? includeProperties = null,
            string? appCode = null, List<long>? allowedAreaIds = null)
        {
            IdQueries.Add(id);
            return _inner.GetByIdAsync(id, includeProperties, appCode, allowedAreaIds);
        }

        public Task<Customer?> GetByCodeAsync(string code) => _inner.GetByCodeAsync(code);

        public Task<bool> CodeExistsAsync(string code, long? excludeCustomerId = null) =>
            _inner.CodeExistsAsync(code, excludeCustomerId);

        public Task<bool> AppCodeExistsAsync(string appCode, long? excludeCustomerId = null) =>
            _inner.AppCodeExistsAsync(appCode, excludeCustomerId);

        public Task<(int DeviceCount, int ProjectCount, int UserCount)> GetCustomerStatsAsync(long customerId) =>
            _inner.GetCustomerStatsAsync(customerId);

        public Task UpdateDeviceCountAsync(long customerId, int increment = 1) =>
            _inner.UpdateDeviceCountAsync(customerId, increment);

        public Task<IEnumerable<Customer>> GetAllAsync(string[]? includeProperties = null,
            string? appCode = null, List<long>? allowedAreaIds = null) =>
            _inner.GetAllAsync(includeProperties, appCode, allowedAreaIds);

        public Task<IEnumerable<Customer>> GetAsync(Expression<Func<Customer, bool>> predicate,
            string[]? includeProperties = null, string? appCode = null, List<long>? allowedAreaIds = null) =>
            _inner.GetAsync(predicate, includeProperties, appCode, allowedAreaIds);

        public Task<(IEnumerable<Customer> Items, int TotalCount, int TotalPages)> GetPagedAsync(
            int pageNumber = 1, int pageSize = 10, Expression<Func<Customer, bool>>? predicate = null,
            Expression<Func<Customer, object>>? orderBy = null, bool isDescending = false,
            string[]? includeProperties = null, string? appCode = null, List<long>? allowedAreaIds = null) =>
            _inner.GetPagedAsync(pageNumber, pageSize, predicate, orderBy, isDescending,
                includeProperties, appCode, allowedAreaIds);

        public IQueryable<Customer> GetQueryable(string? appCode = null, List<long>? allowedAreaIds = null) =>
            _inner.GetQueryable(appCode, allowedAreaIds);

        public Task<Customer> AddAsync(Customer entity) => _inner.AddAsync(entity);
        public Task AddRangeAsync(IEnumerable<Customer> entities) => _inner.AddRangeAsync(entities);
        public Task UpdateAsync(Customer entity) => _inner.UpdateAsync(entity);
        public Task UpdateRangeAsync(IEnumerable<Customer> entities) => _inner.UpdateRangeAsync(entities);
        public Task DeleteAsync(Customer entity) => _inner.DeleteAsync(entity);

        public Task<bool> DeleteByIdAsync(long id, string? appCode = null,
            List<long>? allowedAreaIds = null) => _inner.DeleteByIdAsync(id, appCode, allowedAreaIds);

        public Task DeleteRangeAsync(IEnumerable<Customer> entities) => _inner.DeleteRangeAsync(entities);

        public Task<bool> ExistsAsync(Expression<Func<Customer, bool>> predicate,
            string? appCode = null, List<long>? allowedAreaIds = null) =>
            _inner.ExistsAsync(predicate, appCode, allowedAreaIds);

        public Task<int> CountAsync(Expression<Func<Customer, bool>>? predicate = null,
            string? appCode = null, List<long>? allowedAreaIds = null) =>
            _inner.CountAsync(predicate, appCode, allowedAreaIds);
    }

    /// <summary>
    /// 权限服务替身：记录 GetEffectivePermissionsAsync 的入参（验证三处调用保留且入参为 user.Role），
    /// 返回值与生产静态快照一致（超管全量 / 其余按角色），避免引入 MySQL 依赖。
    /// </summary>
    private sealed class RecordingPermissionService : IPermissionService
    {
        public List<string> RolesAsked { get; } = new();

        public Task<List<string>> GetEffectivePermissionsAsync(string roleCode)
        {
            RolesAsked.Add(roleCode);
            return Task.FromResult(Roles.IsSuperAdmin(roleCode)
                ? Permissions.All
                : Roles.GetRolePermissions(roleCode));
        }

        public List<string> GetRolePermissions(string roleCode) => Roles.GetRolePermissions(roleCode);

        public Dictionary<string, List<string>> GetAllRolePermissions() => new()
        {
            [Roles.SUPER_ADMIN] = Roles.GetSuperAdminPermissions(),
            [Roles.ADMIN] = Roles.GetAdminPermissions(),
            [Roles.USER] = Roles.GetUserPermissions()
        };

        public List<string> GetAllPermissionCodes() => Permissions.All;
        public Dictionary<string, string> GetPermissionDescriptions() => new();
        public bool HasPermission(string roleCode, string permissionCode) =>
            GetRolePermissions(roleCode).Contains(permissionCode);
    }
}
