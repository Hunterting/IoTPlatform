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

        // 获取客户信息
        Customer? customer = null;
        if (user.CustomerId.HasValue)
        {
            customer = await _customerRepository.GetByIdAsync(user.CustomerId.Value);
        }

        // 获取设备数量
        int deviceCount = 0;
        if (customer != null)
        {
            deviceCount = await _deviceRepository.CountAsync(d => d.AppCode == customer.AppCode);
        }

        // 生成JWT Token
        var allowedAreaIdsStr = user.AllowedAreaIds;
        // 【租户隔离修复】AppCode 优先取用户自身字段，customer?.AppCode 兜底。
        // 种子/存量用户可能只有 User.AppCode 而未设 CustomerId（此时 customer 为 null），
        // 旧逻辑只用 customer?.AppCode 会签发不带 AppCode 的 token，导致租户越权看到全部数据。
        //
        // 【超管特例】超管不签发 AppCode claim：ApplyFilters 是「appCode 非空才过滤」，
        // 签发 system 会把超管过滤成只看 AppCode=system 的数据（真实告警均属 customer_xxx），
        // 反而看不到全部。超管看全部由「无 AppCode claim + 全局过滤器跳过」共同保证。
        var effectiveAppCode = user.IsSuperAdmin
            ? null
            : (!string.IsNullOrEmpty(user.AppCode) ? user.AppCode : customer?.AppCode);
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

        // 构建用户DTO
        var userDto = new UserDto
        {
            Id = user.Id,
            Name = user.FullName,
            Email = user.Email,
            Role = user.Role,
            Avatar = user.Avatar,
            AllowedAreaIds = allowedAreaIds,
            IsSuperAdmin = user.IsSuperAdmin
        };

        // 计算并下发当前用户的有效权限（单一事实来源 = DB Role.Permissions）
        userDto.Permissions = await _permissionService.GetEffectivePermissionsAsync(user.Role);

        // 构建当前客户DTO
        CustomerDto? currentCustomer = null;
        if (customer != null)
        {
            currentCustomer = new CustomerDto
            {
                Id = customer.Id,
                Name = customer.Name,
                Code = customer.Code,
                AppCode = customer.AppCode,
                ContactPerson = customer.ContactPerson,
                ContactPhone = customer.ContactPhone,
                ContactEmail = customer.ContactEmail,
                Address = customer.Address,
                Status = customer.Status,
                CreatedAt = customer.CreatedAt,
                DeviceCount = deviceCount
            };
        }

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

        var userDto = new UserDto
        {
            Id = user.Id,
            Name = user.Name,
            Email = user.Email,
            Role = user.Role,
            Avatar = user.Avatar,
            AllowedAreaIds = allowedAreaIds,
            IsSuperAdmin = user.IsSuperAdmin
        };

        // 计算并下发当前用户的有效权限（单一事实来源 = DB Role.Permissions）
        userDto.Permissions = await _permissionService.GetEffectivePermissionsAsync(user.Role);

        CustomerDto? currentCustomer = null;
        if (user.Customer != null)
        {
            var deviceCount = await _deviceRepository.CountAsync(d => d.AppCode == user.Customer.AppCode);
            currentCustomer = new CustomerDto
            {
                Id = user.Customer.Id,
                Name = user.Customer.Name,
                Code = user.Customer.Code,
                AppCode = user.Customer.AppCode,
                ContactPerson = user.Customer.ContactPerson,
                ContactPhone = user.Customer.ContactPhone,
                ContactEmail = user.Customer.ContactEmail,
                Address = user.Customer.Address,
                Status = user.Customer.Status,
                CreatedAt = user.Customer.CreatedAt,
                DeviceCount = deviceCount
            };
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

        var userDto = new UserDto
        {
            Id = user.Id,
            Name = user.FullName,
            Email = user.Email,
            Role = user.Role,
            Avatar = user.Avatar,
            AllowedAreaIds = allowedAreaIds
        };

        // 计算并下发当前用户的有效权限（单一事实来源 = DB Role.Permissions）；
        // SwitchCustomerAsync 中 user.Role 仍是超管自身角色，逻辑一致。
        userDto.Permissions = await _permissionService.GetEffectivePermissionsAsync(user.Role);

        var currentCustomer = new CustomerDto
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

        // 生成新的Token
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
