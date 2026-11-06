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
            permissionService: _permissions);
    }

    public void Dispose()
    {
        _db.Dispose();
        GC.SuppressFinalize(this);
    }

    // ─────────────────────────────────────────────────────────────
    // 一、S1 存量库形态（CustomerId 全 NULL，AppCode 为身份主源）
    // ─────────────────────────────────────────────────────────────

    [Fact(DisplayName = "TC-S1-01 存量库登录 → 回填 appCode/customerId/customerName 且 currentCustomer 非空")]
    public async Task S1_LegacyDb_Login_BackfillsAllTenantFields()
    {
        // Arrange
        var customer = SeedCustomer(1, "customer_001", "海底捞智慧工厂有限公司");
        var user = SeedUser(11, "manager@haidilao.com", Roles.ADMIN,
            isSuperAdmin: false, customerId: null, appCode: "customer_001");
        await CommitAsync();

        // Act
        var response = await LoginAsync(user.Email);

        // Assert：登录响应体字段（修复前此处四项全为 null）
        Assert.Equal("customer_001", response.User.AppCode);
        Assert.Equal<long?>(customer.Id, response.User.CustomerId);
        Assert.False(string.IsNullOrWhiteSpace(response.User.CustomerName));
        Assert.Equal(customer.Name, response.User.CustomerName);
        Assert.NotNull(response.CurrentCustomer);
        Assert.Equal("customer_001", response.CurrentCustomer!.AppCode);
        Assert.Equal(customer.Id, response.CurrentCustomer.Id);
        Assert.Equal(customer.Name, response.CurrentCustomer.Name);

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

    [Fact(DisplayName = "TC-S1-02 存量库登录 → 走 GetByAppCodeAsync 兜底、不触发 GetById，JWT 与 DTO 的 AppCode 一致")]
    public async Task S1_LegacyDb_Login_UsesAppCodeFallbackAndJwtStaysAligned()
    {
        // Arrange
        SeedCustomer(1, "customer_001", "海底捞");
        var user = SeedUser(11, "manager@haidilao.com", Roles.ADMIN,
            isSuperAdmin: false, customerId: null, appCode: "customer_001");
        await CommitAsync();

        // Act
        var response = await LoginAsync(user.Email);

        // Assert：CustomerId 为 NULL ⇒ 跳过 GetByIdAsync，仅反查一次 AppCode
        Assert.Empty(_customers.IdQueries);
        Assert.Equal(new[] { "customer_001" }, _customers.AppCodeQueries);

        // P-2：effectiveAppCode 单点喂给 JWT claim 与 UserDto.AppCode，二者不得漂移
        var token = DecodeToken(response.Token);
        Assert.Equal("customer_001", GetClaim(token, "AppCode"));
        Assert.Equal(response.User.AppCode, GetClaim(token, "AppCode"));
        Assert.Equal(Roles.ADMIN, GetClaim(token, ClaimTypes.Role));
    }

    [Fact(DisplayName = "TC-S1-03 存量库 /auth/me → 与登录响应同样回填（含 currentCustomer 与 permissions）")]
    public async Task S1_LegacyDb_GetCurrentUser_BackfillsIdenticalFields()
    {
        // Arrange
        var customer = SeedCustomer(1, "customer_001", "海底捞");
        var user = SeedUser(11, "manager@haidilao.com", Roles.ADMIN,
            isSuperAdmin: false, customerId: null, appCode: "customer_001");
        await CommitAsync();
        var login = await LoginAsync(user.Email);
        _customers.AppCodeQueries.Clear(); // 隔离本次 /auth/me 的调用记录

        // Act：GetUserDetailsAsync 的 Include(u => u.Customer) 在存量库下同样为 null
        var me = await _sut.GetCurrentUserAsync(user.Id);

        // Assert
        Assert.NotNull(me);
        Assert.Equal(login.User.AppCode, me!.User.AppCode);
        Assert.Equal(login.User.CustomerId, me.User.CustomerId);
        Assert.Equal(login.User.CustomerName, me.User.CustomerName);
        Assert.NotNull(me.CurrentCustomer);
        Assert.Equal("customer_001", me.CurrentCustomer!.AppCode);
        Assert.Equal(customer.Id, me.CurrentCustomer.Id);
        Assert.Equal(login.User.Permissions, me.User.Permissions);
        Assert.Equal(new[] { "customer_001" }, _customers.AppCodeQueries);
        Assert.Equal(new[] { Roles.ADMIN, Roles.ADMIN }, _permissions.RolesAsked);
    }

    // ─────────────────────────────────────────────────────────────
    // 二、S2 新库形态（CustomerId 有值，快速路径）
    // ─────────────────────────────────────────────────────────────

    [Fact(DisplayName = "TC-S2-01 新库登录 → CustomerId 命中且不重复反查 AppCode")]
    public async Task S2_NewDb_Login_UsesCustomerIdFastPath()
    {
        // Arrange
        var customer = SeedCustomer(1, "customer_001", "海底捞");
        var user = SeedUser(11, "manager@haidilao.com", Roles.ADMIN,
            isSuperAdmin: false, customerId: customer.Id, appCode: "customer_001");
        await CommitAsync();

        // Act
        var response = await LoginAsync(user.Email);

        // Assert
        Assert.Equal("customer_001", response.User.AppCode);
        Assert.Equal<long?>(customer.Id, response.User.CustomerId);
        Assert.Equal(customer.Name, response.User.CustomerName);
        Assert.NotNull(response.CurrentCustomer);
        Assert.Equal("customer_001", response.CurrentCustomer!.AppCode);
        Assert.Equal(new[] { customer.Id }, _customers.IdQueries);
        Assert.Empty(_customers.AppCodeQueries);
        Assert.NotEmpty(response.User.Permissions);
    }

    [Fact(DisplayName = "TC-S2-02 新库 /auth/me → 导航属性命中，字段与登录响应一致")]
    public async Task S2_NewDb_GetCurrentUser_UsesNavigationAndMatchesLogin()
    {
        // Arrange
        var customer = SeedCustomer(1, "customer_001", "海底捞");
        var user = SeedUser(11, "manager@haidilao.com", Roles.ADMIN,
            isSuperAdmin: false, customerId: customer.Id, appCode: "customer_001");
        await CommitAsync();
        var login = await LoginAsync(user.Email);

        // Act
        var me = await _sut.GetCurrentUserAsync(user.Id);

        // Assert
        Assert.NotNull(me);
        Assert.Equal(login.User.AppCode, me!.User.AppCode);
        Assert.Equal(login.User.CustomerId, me.User.CustomerId);
        Assert.Equal(login.User.CustomerName, me.User.CustomerName);
        Assert.NotNull(me.CurrentCustomer);
        Assert.Equal(login.CurrentCustomer!.AppCode, me.CurrentCustomer.AppCode);
        Assert.Equal(login.User.Permissions, me.User.Permissions);
        Assert.Empty(_customers.AppCodeQueries); // user.Customer 已由 Include 加载，无需兜底
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

    [Fact(DisplayName = "TC-S4-01 AppCode 在 customers 表无对应行 → 不抛异常、字段按 P-2 回落")]
    public async Task S4_UnmatchedAppCode_DoesNotThrowAndFallsBackGracefully()
    {
        // Arrange
        SeedCustomer(1, "customer_001", "租户A"); // 与用户 AppCode 不匹配
        var user = SeedUser(12, "orphan@tenant.local", Roles.ADMIN,
            isSuperAdmin: false, customerId: null, appCode: "customer_999");
        await CommitAsync();

        // Act
        var response = await LoginAsync(user.Email);

        // Assert：不抛异常，AppCode 仍来自 user.AppCode（与 JWT 保持一致）
        Assert.Equal("customer_999", response.User.AppCode);
        Assert.Null(response.User.CustomerId);
        Assert.Null(response.User.CustomerName);
        Assert.Null(response.CurrentCustomer);
        Assert.Equal(new[] { "customer_999" }, _customers.AppCodeQueries);
        Assert.Equal("customer_999", GetClaim(DecodeToken(response.Token), "AppCode"));
        Assert.NotEmpty(response.User.Permissions);
    }

    [Fact(DisplayName = "TC-S4-02 CustomerId 悬空指向不存在客户 → 回落 AppCode 反查成功")]
    public async Task S4_DanglingCustomerId_FallsBackToAppCodeLookup()
    {
        // Arrange：§5 表 S4 边界
        var customer = SeedCustomer(2, "customer_002", "绿能科技");
        var user = SeedUser(13, "chef@greenenergy.com", Roles.STAFF,
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
        var tenant = SeedUser(13, "chef@greenenergy.com", Roles.STAFF,
            isSuperAdmin: false, customerId: null, appCode: "customer_002");
        var admin = SeedUser(1, "admin@system.com", Roles.SUPER_ADMIN,
            isSuperAdmin: true, customerId: null, appCode: "system");
        await CommitAsync();

        // Act
        var login = await LoginAsync(tenant.Email);
        var me = await _sut.GetCurrentUserAsync(tenant.Id);
        var switched = await _sut.SwitchCustomerAsync(admin.Id, 2);

        // Assert：三处调用入参严格是 user.Role，且权限集合与改动前静态快照一致
        Assert.Equal(new[] { Roles.STAFF, Roles.STAFF, Roles.SUPER_ADMIN }, _permissions.RolesAsked);
        Assert.Equal(Roles.GetStaffPermissions(), login.User.Permissions);
        Assert.Equal(Roles.GetStaffPermissions(), me!.User.Permissions);
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
        var tenant = SeedUser(13, "chef@greenenergy.com", Roles.STAFF,
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
            [Roles.STAFF] = Roles.GetStaffPermissions()
        };

        public List<string> GetAllPermissionCodes() => Permissions.All;
        public Dictionary<string, string> GetPermissionDescriptions() => new();
        public bool HasPermission(string roleCode, string permissionCode) =>
            GetRolePermissions(roleCode).Contains(permissionCode);
    }
}
