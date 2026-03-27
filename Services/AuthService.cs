using IoTPlatform.Data;
using IoTPlatform.DTOs.Requests;
using IoTPlatform.DTOs.Responses;
using IoTPlatform.Infrastructure.Cache;
using IoTPlatform.Infrastructure.JWT;
using IoTPlatform.Models;
using Microsoft.EntityFrameworkCore;

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
/// 认证服务实现
/// </summary>
public class AuthService : IAuthService
{
    private readonly AppDbContext _dbContext;
    private readonly JwtHelper _jwtHelper;
    private readonly IRedisCacheService _cache;

    public AuthService(
        AppDbContext dbContext,
        JwtHelper jwtHelper,
        IRedisCacheService cache)
    {
        _dbContext = dbContext;
        _jwtHelper = jwtHelper;
        _cache = cache;
    }

    /// <summary>
    /// 用户登录
    /// </summary>
    public async Task<LoginResponse> LoginAsync(LoginRequest request)
    {
        // 查找用户
        var user = await _dbContext.Users
            .Include(u => u.Customer)
            .FirstOrDefaultAsync(u => u.Email == request.Email && u.IsActive);

        if (user == null)
        {
            throw new UnauthorizedAccessException("邮箱或密码错误");
        }

        // 验证密码
        if (!BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
        {
            throw new UnauthorizedAccessException("邮箱或密码错误");
        }

        // 生成JWT Token
        var allowedAreaIdsStr = user.AllowedAreaIds;
        var token = _jwtHelper.GenerateToken(
            user.Id,
            user.Email,
            user.Role,
            user.CustomerId,
            user.Customer?.AppCode,
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
        if (user.Customer != null)
        {
            var deviceCount = await _dbContext.Devices.CountAsync(d => d.AppCode == user.Customer.AppCode);
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
        var user = await _dbContext.Users
            .Include(u => u.Customer)
            .FirstOrDefaultAsync(u => u.Id == userId);

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
            var deviceCount = await _dbContext.Devices.CountAsync(d => d.AppCode == user.Customer.AppCode);
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
        var user = await _dbContext.Users
            .Include(u => u.Customer)
            .FirstOrDefaultAsync(u => u.Id == userId);

        if (user == null) return null;
        if (user.Role != "super_admin")
        {
            throw new UnauthorizedAccessException("只有超级管理员可以切换客户");
        }

        var customer = await _dbContext.Customers.FirstOrDefaultAsync(c => c.Id == customerId);
        if (customer == null) return null;

        var deviceCount = await _dbContext.Devices.CountAsync(d => d.AppCode == customer.AppCode);

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
            var log = new LoginLog
            {
                UserId = user.Id,
                Email = user.Email,
                UserName = user.Name,
                Status = success ? "success" : "failed",
                LoginTime = DateTime.UtcNow
            };

            _dbContext.LoginLogs.Add(log);
            await _dbContext.SaveChangesAsync();
        }
        catch
        {
            // 记录日志失败不影响主流程
        }
    }
}
