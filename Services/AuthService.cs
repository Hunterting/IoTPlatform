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

    public AuthService(
        IUserRepository userRepository,
        ICustomerRepository customerRepository,
        IDeviceRepository deviceRepository,
        ILogRepository logRepository,
        IUnitOfWork unitOfWork,
        JwtHelper jwtHelper,
        IRedisCacheService cache)
    {
        _userRepository = userRepository;
        _customerRepository = customerRepository;
        _deviceRepository = deviceRepository;
        _logRepository = logRepository;
        _unitOfWork = unitOfWork;
        _jwtHelper = jwtHelper;
        _cache = cache;
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
        if (!BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
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
        var token = _jwtHelper.GenerateToken(
            user.Id,
            user.Email,
            user.Role,
            user.CustomerId,
            customer?.AppCode,
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
            Name = user.Name,
            Email = user.Email,
            Role = user.Role,
            Avatar = user.Avatar,
            AllowedAreaIds = allowedAreaIds
        };

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
                Contact = customer.Contact,
                Phone = customer.Phone,
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
            AllowedAreaIds = allowedAreaIds
        };

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
                Contact = user.Customer.Contact,
                Phone = user.Customer.Phone,
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
        if (user.Role != "super_admin")
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
            Name = user.Name,
            Email = user.Email,
            Role = user.Role,
            Avatar = user.Avatar,
            AllowedAreaIds = allowedAreaIds
        };

        var currentCustomer = new CustomerDto
        {
            Id = customer.Id,
            Name = customer.Name,
            Code = customer.Code,
            AppCode = customer.AppCode,
            Contact = customer.Contact,
            Phone = customer.Phone,
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
                userName: user.Name,
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
