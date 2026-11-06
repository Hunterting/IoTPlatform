using IoTPlatform.Configuration;
using IoTPlatform.Data.Repositories.Interfaces;
using IoTPlatform.DTOs.Requests;
using IoTPlatform.DTOs.Responses;
using IoTPlatform.Infrastructure.Cache;
using IoTPlatform.Infrastructure.JWT;
using IoTPlatform.Models;

namespace IoTPlatform.Services;

/// <summary>
/// 认证服务接口
/// </summary>
public interface IAuthService
{
    Task<LoginResponse> LoginAsync(LoginRequest request);
    Task<LoginResponse?> GetCurrentUserAsync(long userId);
    Task<LoginResponse?> SwitchCustomerAsync(long userId, long customerId);
}

/// <summary>
/// 认证服务实现（使用仓储模式）
/// </summary>
public class AuthService : IAuthService
{
    private readonly IUserRepository _userRepository;
    private readonly ICustomerRepository _customerRepository;
    private readonly IDeviceRepository _deviceRepository;
    private readonly ILogRepository _logRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly JwtHelper _jwtHelper;
    private readonly IRedisCacheService _cache;
    private readonly IPermissionService _permissionService;

    public AuthService(
        IUserRepository userRepository,
        ICustomerRepository customerRepository,
        IDeviceRepository deviceRepository,
        ILogRepository logRepository,
        IUnitOfWork unitOfWork,
        JwtHelper jwtHelper,
        IRedisCacheService cache,
        IPermissionService permissionService)
    {
        _userRepository = userRepository;
        _customerRepository = customerRepository;
        _deviceRepository = deviceRepository;
        _logRepository = logRepository;
        _unitOfWork = unitOfWork;
        _jwtHelper = jwtHelper;
        _cache = cache;
        _permissionService = permissionService;
    }

    /// <summary>
    /// 用户登录
    /// </summary>
    public async Task<LoginResponse> LoginAsync(LoginRequest request)
    {
        // 查找用户
        var user = await _userRepository.GetByEmailAsync(request.Email);

        if (user == null || !user.IsActive)
        {
            throw new UnauthorizedAccessException("邮箱或密码错误");
        }

        // 验证密码
        var inputPassword = request.Password?.Trim() ?? string.Empty;
        if (!BCrypt.Net.BCrypt.Verify(inputPassword, user.Password))
        {
            throw new UnauthorizedAccessException("邮箱或密码错误");
        }

        // 获取客户信息：CustomerId 优先（新库 / 刷新种子后的快速路径），AppCode 兜底反查（存量库 CustomerId 全 NULL）
        Customer? customer = null;
        if (user.CustomerId.HasValue)
        {
            customer = await _customerRepository.GetByIdAsync(user.CustomerId.Value);
        }

        // 【租户隔离修复】AppCode 优先取用户自身字段，customer?.AppCode 兜底。
        // 种子/存量用户可能只有 User.AppCode 而未设 CustomerId（此时 customer 为 null），
        // 旧逻辑只用 customer?.AppCode 会签发不带 AppCode 的 token，导致租户越权看到全部数据。
        //
        // 【超管特例】超管不签发 AppCode claim：ApplyFilters 是「appCode 非空才过滤」，
        // 签发 system 会把超管过滤成只看 AppCode=system 的数据（真实告警均属 customer_xxx），
        // 反而看不到全部。超管看全部由「无 AppCode claim + 全局过滤器跳过」共同保证。
        //
        // P-2【单一计算】effectiveAppCode 只在此处算一次，同时喂 JWT claim 与 UserDto.AppCode，
        //     从结构上杜绝「token 对、响应体错」再次漂移。
        var effectiveAppCode = ComputeEffectiveAppCode(user, customer);

        // P-1【身份主源】租户身份唯一事实来源是 AppCode（users.AppCode ↔ customers.AppCode），
        //     存量库用户 CustomerId 全 NULL 而 AppCode 全有值；CustomerId 只是新库 / 刷新种子后的
        //     快速路径与兼容兜底，绝不作为身份判定主键或唯一入口。
        // P-3【超管不变量】IsSuperAdmin 短路 → effectiveAppCode = null → 不反查、不签发 AppCode claim、
        //     CurrentCustomer = null，登录态超管语义逐字不变。
        // P-4【服务端隔离为准】后端按 JWT AppCode claim 隔离（CustomersController / ApplyFilters），
        //     前端过滤只做防御性二次校验，不负责授权。
        // P-5【零写库】不改表结构、不刷数据、不执行任何 UPDATE/INSERT；两套数据形态都在代码层兼容。
        // 【存量库兜底】customer 为 null 时用 AppCode 反查 Customer；
        //     ICustomerRepository.GetByAppCodeAsync 已存在，零新增仓储方法 / 零 migration / 零写库。
        if (customer == null && !user.IsSuperAdmin && !string.IsNullOrEmpty(effectiveAppCode))
        {
            customer = await _customerRepository.GetByAppCodeAsync(effectiveAppCode!);
        }

        // 获取设备数量（customer 已被兜底解析到，存量库也能算出本租户设备数）
        int deviceCount = 0;
        if (customer != null)
        {
            deviceCount = await _deviceRepository.CountAsync(d => d.AppCode == customer.AppCode);
        }

        // 生成JWT Token（GenerateToken 的 6 个参数值与修复前逐字一致，JWT claim 零变更）
        var allowedAreaIdsStr = user.AllowedAreaIds;
        var token = _jwtHelper.GenerateToken(
            user.Id,
            user.Email,
            user.Role,
            user.CustomerId,
            effectiveAppCode,
            allowedAreaIdsStr);

        // 解析允许的区域ID
        List<long>? allowedAreaIds = null;
        if (!string.IsNullOrEmpty(allowedAreaIdsStr))
        {
            allowedAreaIds = allowedAreaIdsStr.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(id => long.Parse(id.Trim()))
                .ToList();
        }

        // 构建用户DTO（三处登录链路统一组装，见 BuildUserDto）
        var userDto = BuildUserDto(user, effectiveAppCode, customer, allowedAreaIds);

        // 计算并下发当前用户的有效权限（单一事实来源 = DB Role.Permissions）
        userDto.Permissions = await _permissionService.GetEffectivePermissionsAsync(user.Role);

        // 构建当前客户DTO
        CustomerDto? currentCustomer = customer != null
            ? BuildCustomerDto(customer, deviceCount)
            : null;

        // 记录登录日志
        await RecordLoginLogAsync(user, true);

        return new LoginResponse
        {
            Token = token,
            User = userDto,
            CurrentCustomer = currentCustomer
        };
    }

    /// <summary>
    /// 获取当前用户信息
    /// </summary>
    public async Task<LoginResponse?> GetCurrentUserAsync(long userId)
    {
        var user = await _userRepository.GetUserDetailsAsync(userId);

        if (user == null) return null;

        var allowedAreaIdsStr = user.AllowedAreaIds;
        List<long>? allowedAreaIds = null;
        if (!string.IsNullOrEmpty(allowedAreaIdsStr))
        {
            allowedAreaIds = allowedAreaIdsStr.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(id => long.Parse(id.Trim()))
                .ToList();
        }

        // /auth/me 与 LoginAsync 共用同一套身份解析：GetUserDetailsAsync 的 Include(u => u.Customer)
        // 在存量库 CustomerId = NULL 时同样得到 user.Customer = null，故按 P-1 / P-5 用 AppCode 兜底反查。
        var effectiveAppCode = ComputeEffectiveAppCode(user, user.Customer);

        // 局部 customer 变量：后续 :196-214 对 user.Customer 的引用全部改用它（兼容新旧两种库形态）
        Customer? customer = user.Customer;
        if (customer == null && !user.IsSuperAdmin && !string.IsNullOrEmpty(effectiveAppCode))
        {
            customer = await _customerRepository.GetByAppCodeAsync(effectiveAppCode!);
        }

        var userDto = BuildUserDto(user, effectiveAppCode, customer, allowedAreaIds);

        // 计算并下发当前用户的有效权限（单一事实来源 = DB Role.Permissions）
        userDto.Permissions = await _permissionService.GetEffectivePermissionsAsync(user.Role);

        CustomerDto? currentCustomer = null;
        if (customer != null)
        {
            var deviceCount = await _deviceRepository.CountAsync(d => d.AppCode == customer.AppCode);
            currentCustomer = BuildCustomerDto(customer, deviceCount);
        }

        return new LoginResponse
        {
            User = userDto,
            CurrentCustomer = currentCustomer
        };
    }

    /// <summary>
    /// 切换客户（仅超级管理员）
    /// </summary>
    public async Task<LoginResponse?> SwitchCustomerAsync(long userId, long customerId)
    {
        var user = await _userRepository.GetByIdAsync(userId);

        if (user == null) return null;
        if (user.Role != Roles.SUPER_ADMIN)
        {
            throw new UnauthorizedAccessException("只有超级管理员可以切换客户");
        }

        var customer = await _customerRepository.GetByIdAsync(customerId);
        if (customer == null) return null;

        var deviceCount = await _deviceRepository.CountAsync(d => d.AppCode == customer.AppCode);

        var allowedAreaIdsStr = user.AllowedAreaIds;
        List<long>? allowedAreaIds = null;
        if (!string.IsNullOrEmpty(allowedAreaIdsStr))
        {
            allowedAreaIds = allowedAreaIdsStr.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(id => long.Parse(id.Trim()))
                .ToList();
        }

        // 「以租户视角查看」的显式例外：DTO 的 CustomerId / AppCode 与本接口新签发的 token
        // （见下方 GenerateToken 的 customerId / customer.AppCode 入参）严格对齐。
        var userDto = BuildUserDto(user, customer.AppCode, customer, allowedAreaIds, customerId);

        // 计算并下发当前用户的有效权限（单一事实来源 = DB Role.Permissions）；
        // SwitchCustomerAsync 中 user.Role 仍是超管自身角色，逻辑一致。
        userDto.Permissions = await _permissionService.GetEffectivePermissionsAsync(user.Role);

        // includeContactEmail: false 逐字保留本接口原有的 CurrentCustomer 字段集合（零回归）
        var currentCustomer = BuildCustomerDto(customer, deviceCount, includeContactEmail: false);

        // 生成新的Token（参数不变）
        var token = _jwtHelper.GenerateToken(
            user.Id,
            user.Email,
            user.Role,
            customerId,
            customer.AppCode,
            null);

        return new LoginResponse
        {
            Token = token,
            User = userDto,
            CurrentCustomer = currentCustomer
        };
    }

    /// <summary>
    /// 计算有效 AppCode（P-1 身份主源 / P-2 单一计算 / P-3 超管不变量）。
    /// 三处登录链路共用同一表达式，保证 JWT claim 与 UserDto.AppCode 永不漂移。
    /// </summary>
    /// <param name="user">登录用户</param>
    /// <param name="byId">按 CustomerId（或导航属性）解析到的客户，可为 null</param>
    /// <returns>超管恒 null；租户优先 users.AppCode，其次客户的 AppCode</returns>
    private static string? ComputeEffectiveAppCode(User user, Customer? byId) =>
        user.IsSuperAdmin
            ? null
            : (!string.IsNullOrEmpty(user.AppCode) ? user.AppCode : byId?.AppCode);

    /// <summary>
    /// 统一构建 UserDto。三处手工 new UserDto 曾漏填 AppCode / CustomerId / CustomerName，
    /// 抽成单一组装点防止再次漂移。不引入 AutoMapper：MappingProfile 依赖 Customer 导航
    /// （存量库为 null 时 CustomerName 仍丢），且「超管置 null」的业务特例无法用 MapFrom 表达。
    /// </summary>
    /// <param name="user">登录用户</param>
    /// <param name="effectiveAppCode">单点计算出的有效 AppCode（超管为 null）</param>
    /// <param name="customer">已解析的客户（CustomerId 优先 + AppCode 兜底反查结果），可为 null</param>
    /// <param name="allowedAreaIds">解析后的允许区域 ID 列表</param>
    /// <param name="customerIdOverride">SwitchCustomerAsync 传入的显式 customerId；其余路径传 null</param>
    private static UserDto BuildUserDto(
        User user,
        string? effectiveAppCode,
        Customer? customer,
        List<long>? allowedAreaIds,
        long? customerIdOverride = null) => new()
    {
        Id = user.Id,
        Name = user.FullName,
        Email = user.Email,
        Role = user.Role,
        CustomerId = customerIdOverride ?? customer?.Id ?? user.CustomerId,
        CustomerName = customer?.Name,
        AppCode = effectiveAppCode,
        Avatar = user.Avatar,
        AllowedAreaIds = allowedAreaIds,
        IsActive = user.IsActive,
        IsSuperAdmin = user.IsSuperAdmin,
        CreatedAt = user.CreatedAt,
        UpdatedAt = user.UpdatedAt
    };

    /// <summary>
    /// 统一构建 LoginResponse.CurrentCustomer。
    /// </summary>
    /// <param name="customer">客户实体</param>
    /// <param name="deviceCount">该租户的设备数量</param>
    /// <param name="includeContactEmail">SwitchCustomerAsync 传 false 以逐字保留其原有字段集合</param>
    private static CustomerDto BuildCustomerDto(
        Customer customer,
        int deviceCount,
        bool includeContactEmail = true)
    {
        var dto = new CustomerDto
        {
            Id = customer.Id,
            Name = customer.Name,
            Code = customer.Code,
            AppCode = customer.AppCode,
            ContactPerson = customer.ContactPerson,
            ContactPhone = customer.ContactPhone,
            Address = customer.Address,
            Status = customer.Status,
            CreatedAt = customer.CreatedAt,
            DeviceCount = deviceCount
        };

        if (includeContactEmail)
        {
            dto.ContactEmail = customer.ContactEmail;
        }

        return dto;
    }

    /// <summary>
    /// 记录登录日志
    /// </summary>
    private async Task RecordLoginLogAsync(User user, bool success)
    {
        try
        {
            await _logRepository.LogLoginAsync(
                userId: user.Id,
                email: user.Email,
                userName: user.FullName,
                ipAddress: null,
                userAgent: null,
                status: success ? "success" : "failed"
            );
        }
        catch
        {
            // 记录日志失败不影响主流程
        }
    }
}
