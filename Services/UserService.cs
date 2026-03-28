using IoTPlatform.Data.Repositories.Interfaces;
using IoTPlatform.DTOs.Requests;
using IoTPlatform.DTOs.Responses;
using IoTPlatform.Helpers;
using IoTPlatform.Models;
using Microsoft.EntityFrameworkCore;

namespace IoTPlatform.Services;

/// <summary>
/// 用户服务实现（使用仓储模式）
/// </summary>
public class UserService : IUserService
{
    private readonly IUserRepository _userRepository;
    private readonly ICustomerRepository _customerRepository;
    private readonly ILogRepository _logRepository;
    private readonly IWorkOrderRepository _workOrderRepository;
    private readonly IUnitOfWork _unitOfWork;

    public UserService(
        IUserRepository userRepository,
        ICustomerRepository customerRepository,
        ILogRepository logRepository,
        IWorkOrderRepository workOrderRepository,
        IUnitOfWork unitOfWork)
    {
        _userRepository = userRepository;
        _customerRepository = customerRepository;
        _logRepository = logRepository;
        _workOrderRepository = workOrderRepository;
        _unitOfWork = unitOfWork;
    }

    /// <summary>
    /// 获取用户列表
    /// </summary>
    public async Task<PagedResponse<UserDto>> GetUsersAsync(int page, int pageSize, string? keyword, string? appCode, string? currentUserRole)
    {
        // 确定查询的appCode
        var queryAppCode = appCode;
        if (currentUserRole != Configuration.Roles.SUPER_ADMIN && string.IsNullOrEmpty(appCode))
        {
            queryAppCode = null;
        }

        // 构建查询条件
        var query = _userRepository.GetQueryable(queryAppCode);

        // 关键词搜索
        if (!string.IsNullOrEmpty(keyword))
        {
            query = query.Where(u =>
                u.FullName.Contains(keyword) ||
                u.Email.Contains(keyword));
        }

        var totalCount = await query.CountAsync();
        var users = await query
            .OrderByDescending(u => u.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        // 转换为DTO
        var userDtos = new List<UserDto>();
        foreach (var user in users)
        {
            customer? customer = null;
            if (user.CustomerId.HasValue)
            {
                customer = await _customerRepository.GetByIdAsync(user.CustomerId.Value);
            }

            var userDto = new UserDto
            {
                Id = user.Id,
                Name = user.FullName,
                Email = user.Email,
                Role = user.Role,
                CustomerId = user.CustomerId,
                CustomerName = customer?.Name,
                AppCode = user.AppCode,
                Avatar = user.Avatar,
                AllowedAreaIds = !string.IsNullOrEmpty(user.AllowedAreaIds)
                    ? user.AllowedAreaIds.Split(',', StringSplitOptions.RemoveEmptyEntries)
                        .Select(id => long.Parse(id.Trim()))
                        .ToList()
                    : null,
                IsActive = user.IsActive,
                CreatedAt = user.CreatedAt,
                UpdatedAt = user.UpdatedAt
            };
            userDtos.Add(userDto);
        }

        return PagedResponse<UserDto>.Create(userDtos, totalCount, page, pageSize);
    }

    /// <summary>
    /// 获取用户详情
    /// </summary>
    public async Task<UserDto?> GetUserAsync(long id, string? appCode, string? currentUserRole)
    {
        var queryAppCode = appCode;
        if (currentUserRole == Configuration.Roles.SUPER_ADMIN)
        {
            queryAppCode = null;
        }

        var user = await _userRepository.GetUserDetailsAsync(id, queryAppCode);
        if (user == null) return null;

        return new UserDto
        {
            Id = user.Id,
            Name = user.FullName,
            Email = user.Email,
            Role = user.Role,
            CustomerId = user.CustomerId,
            CustomerName = user.Customer?.Name,
            AppCode = user.AppCode,
            Avatar = user.Avatar,
            AllowedAreaIds = !string.IsNullOrEmpty(user.AllowedAreaIds)
                ? user.AllowedAreaIds.Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(id => long.Parse(id.Trim()))
                    .ToList()
                : null,
            IsActive = user.IsActive,
            CreatedAt = user.CreatedAt,
            UpdatedAt = user.UpdatedAt
        };
    }

    /// <summary>
    /// 创建用户
    /// </summary>
    public async Task<UserDto> CreateUserAsync(CreateUserRequest request)
    {
        // 检查邮箱是否已存在
        var exists = await _userRepository.EmailExistsAsync(request.Email);
        if (exists)
        {
            throw new InvalidOperationException("邮箱已存在");
        }

        // 获取客户信息
        Models.Customer? customer = null;
        if (request.CustomerId.HasValue)
        {
            customer = await _customerRepository.GetByIdAsync(request.CustomerId.Value);
            if (customer == null)
            {
                throw new InvalidOperationException("客户不存在");
            }
        }

        // 密码哈希
        var passwordHash = BCrypt.Net.BCrypt.HashPassword(request.Password);

        var user = new User
        {
            FullName = request.Name,
            Email = request.Email,
            Password = passwordHash,
            Role = request.Role,
            CustomerId = request.CustomerId,
            AppCode = customer?.AppCode,
            Avatar = request.Avatar,
            AllowedAreaIds = request.AllowedAreaIds,
            IsActive = request.IsActive,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _userRepository.AddAsync(user);
        await _unitOfWork.SaveChangesAsync();

        return new UserDto
        {
            Id = user.Id,
            Name = user.Name,
            Email = user.Email,
            Role = user.Role,
            CustomerId = user.CustomerId,
            CustomerName = customer?.Name,
            AppCode = user.AppCode,
            Avatar = user.Avatar,
            AllowedAreaIds = !string.IsNullOrEmpty(user.AllowedAreaIds)
                ? user.AllowedAreaIds.Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(id => long.Parse(id.Trim()))
                    .ToList()
                : null,
            IsActive = user.IsActive,
            CreatedAt = user.CreatedAt,
            UpdatedAt = user.UpdatedAt
        };
    }

    /// <summary>
    /// 更新用户
    /// </summary>
    public async Task<UserDto> UpdateUserAsync(long id, UpdateUserRequest request, string? appCode, string? currentUserRole)
    {
        var user = await _userRepository.GetByIdAsync(id);
        if (user == null)
        {
            throw new InvalidOperationException("用户不存在");
        }

        // 权限检查
        if (currentUserRole != Configuration.Roles.SUPER_ADMIN && user.AppCode != appCode)
        {
            throw new UnauthorizedAccessException("无权修改该用户");
        }

        // 检查邮箱是否被其他用户使用
        var emailExists = await _userRepository.EmailExistsAsync(request.Email, id);
        if (emailExists)
        {
            throw new InvalidOperationException("邮箱已被其他用户使用");
        }

        user.FullName = request.Name;
        user.Email = request.Email;
        user.Avatar = request.Avatar;
        user.AllowedAreaIds = request.AllowedAreaIds;
        user.IsActive = request.IsActive;
        user.UpdatedAt = DateTime.UtcNow;

        // 角色更新需要谨慎,这里只允许超级管理员修改
        if (!string.IsNullOrEmpty(request.Role) && currentUserRole == Configuration.Roles.SUPER_ADMIN)
        {
            user.Role = request.Role;
        }

        await _userRepository.UpdateAsync(user);
        await _unitOfWork.SaveChangesAsync();

        // 获取客户信息用于DTO
        Customer? customer = null;
        if (user.CustomerId.HasValue)
        {
            customer = await _customerRepository.GetByIdAsync(user.CustomerId.Value);
        }

        return new UserDto
        {
            Id = user.Id,
            Name = user.FullName,
            Email = user.Email,
            Role = user.Role,
            CustomerId = user.CustomerId,
            CustomerName = customer?.Name,
            AppCode = user.AppCode,
            Avatar = user.Avatar,
            AllowedAreaIds = !string.IsNullOrEmpty(user.AllowedAreaIds)
                ? user.AllowedAreaIds.Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(id => long.Parse(id.Trim()))
                    .ToList()
                : null,
            IsActive = user.IsActive,
            CreatedAt = user.CreatedAt,
            UpdatedAt = user.UpdatedAt
        };
    }

    /// <summary>
    /// 删除用户
    /// </summary>
    public async Task DeleteUserAsync(long id, string? appCode, string? currentUserRole)
    {
        var user = await _userRepository.GetByIdAsync(id);
        if (user == null)
        {
            throw new InvalidOperationException("用户不存在");
        }

        // 权限检查
        if (currentUserRole != Configuration.Roles.SUPER_ADMIN && user.AppCode != appCode)
        {
            throw new UnauthorizedAccessException("无权删除该用户");
        }

        // 检查是否有关联数据（工单、操作日志等）
        var hasWorkOrders = await _workOrderRepository.ExistsAsync(w => w.CreatedBy == id);
        var hasAlertLogs = await _logRepository.ExistsAsync(l => l.UserId == id);

        if (hasWorkOrders || hasAlertLogs)
        {
            throw new InvalidOperationException("用户有关联数据，无法删除");
        }

        await _userRepository.DeleteAsync(user);
        await _unitOfWork.SaveChangesAsync();
    }

    /// <summary>
    /// 修改密码
    /// </summary>
    public async Task ChangePasswordAsync(long id, string oldPassword, string newPassword, string? appCode, string? currentUserRole)
    {
        var user = await _userRepository.GetByIdAsync(id);
        if (user == null)
        {
            throw new InvalidOperationException("用户不存在");
        }

        // 权限检查：只能修改自己的密码或超级管理员可以修改
        // 这里简化处理，允许修改

        // 验证旧密码
        if (!BCrypt.Net.BCrypt.Verify(oldPassword, user.Password))
        {
            throw new UnauthorizedAccessException("原密码错误");
        }

        // 设置新密码
        var passwordHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
        await _userRepository.UpdatePasswordAsync(id, passwordHash);
        await _unitOfWork.SaveChangesAsync();
    }
}
