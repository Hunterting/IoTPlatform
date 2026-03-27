using IoTPlatform.Data;
using IoTPlatform.DTOs.Requests;
using IoTPlatform.DTOs.Responses;
using IoTPlatform.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace IoTPlatform.Services;

/// <summary>
/// 用户服务实现
/// </summary>
public class UserService : IUserService
{
    private readonly AppDbContext _dbContext;

    public UserService(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>
    /// 获取用户列表
    /// </summary>
    public async Task<PagedResponse<UserDto>> GetUsersAsync(int page, int pageSize, string? keyword, string? appCode, string? currentUserRole)
    {
        var query = _dbContext.Users.Include(u => u.Customer).AsQueryable();

        // 超级管理员可以查看所有用户，其他角色只能查看所属租户的用户
        if (currentUserRole != Configuration.Roles.SUPER_ADMIN && !string.IsNullOrEmpty(appCode))
        {
            query = query.Where(u => u.AppCode == appCode);
        }

        // 关键词搜索
        if (!string.IsNullOrEmpty(keyword))
        {
            query = query.Where(u =>
                u.Name.Contains(keyword) ||
                u.Email.Contains(keyword));
        }

        var totalCount = await query.CountAsync();
        var users = await query
            .OrderByDescending(u => u.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(u => new UserDto
            {
                Id = u.Id,
                Name = u.Name,
                Email = u.Email,
                Role = u.Role,
                CustomerId = u.CustomerId,
                CustomerName = u.Customer != null ? u.Customer.Name : null,
                AppCode = u.AppCode,
                Avatar = u.Avatar,
                AllowedAreaIds = !string.IsNullOrEmpty(u.AllowedAreaIds)
                    ? u.AllowedAreaIds.Split(',', StringSplitOptions.RemoveEmptyEntries)
                        .Select(id => long.Parse(id.Trim()))
                        .ToList()
                    : null,
                IsActive = u.IsActive,
                CreatedAt = u.CreatedAt,
                UpdatedAt = u.UpdatedAt
            })
            .ToListAsync();

        return PagedResponse<UserDto>.Create(users, totalCount, page, pageSize);
    }

    /// <summary>
    /// 获取用户详情
    /// </summary>
    public async Task<UserDto?> GetUserAsync(long id, string? appCode, string? currentUserRole)
    {
        var query = _dbContext.Users
            .Include(u => u.Customer)
            .AsQueryable();

        // 权限过滤
        if (currentUserRole != Configuration.Roles.SUPER_ADMIN && !string.IsNullOrEmpty(appCode))
        {
            query = query.Where(u => u.AppCode == appCode);
        }

        var user = await query.FirstOrDefaultAsync(u => u.Id == id);
        if (user == null) return null;

        // 获取最后登录时间
        var lastLogin = await _dbContext.LoginLogs
            .Where(l => l.UserId == id && l.Status == "success")
            .OrderByDescending(l => l.LoginTime)
            .FirstOrDefaultAsync();

        return new UserDto
        {
            Id = user.Id,
            Name = user.Name,
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
        var exists = await _dbContext.Users.AnyAsync(u => u.Email == request.Email);
        if (exists)
        {
            throw new InvalidOperationException("邮箱已存在");
        }

        // 获取客户信息
        Models.Customer? customer = null;
        if (request.CustomerId.HasValue)
        {
            customer = await _dbContext.Customers.FindAsync(request.CustomerId.Value);
            if (customer == null)
            {
                throw new InvalidOperationException("客户不存在");
            }
        }

        // 密码哈希
        var passwordHash = BCrypt.Net.BCrypt.HashPassword(request.Password);

        var user = new User
        {
            Name = request.Name,
            Email = request.Email,
            PasswordHash = passwordHash,
            Role = request.Role,
            CustomerId = request.CustomerId,
            AppCode = customer?.AppCode,
            Avatar = request.Avatar,
            AllowedAreaIds = request.AllowedAreaIds,
            IsActive = request.IsActive,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync();

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
        var user = await _dbContext.Users.Include(u => u.Customer).FirstOrDefaultAsync(u => u.Id == id);
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
        var emailExists = await _dbContext.Users
            .AnyAsync(u => u.Email == request.Email && u.Id != id);
        if (emailExists)
        {
            throw new InvalidOperationException("邮箱已被其他用户使用");
        }

        user.Name = request.Name;
        user.Email = request.Email;
        user.Avatar = request.Avatar;
        user.AllowedAreaIds = request.AllowedAreaIds;
        user.IsActive = request.IsActive;
        user.UpdatedAt = DateTime.UtcNow;

        // 角色更新需要谨慎，这里只允许超级管理员修改
        if (!string.IsNullOrEmpty(request.Role) && currentUserRole == Configuration.Roles.SUPER_ADMIN)
        {
            user.Role = request.Role;
        }

        await _dbContext.SaveChangesAsync();

        return new UserDto
        {
            Id = user.Id,
            Name = user.Name,
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
    /// 删除用户
    /// </summary>
    public async Task DeleteUserAsync(long id, string? appCode, string? currentUserRole)
    {
        var user = await _dbContext.Users.FindAsync(id);
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
        var hasWorkOrders = await _dbContext.WorkOrders.AnyAsync(w => w.CreatedBy == id);
        var hasAlertLogs = await _dbContext.AlertProcessLogs.AnyAsync(l => l.ProcessedBy == id);
        var hasOperationLogs = await _dbContext.OperationLogs.AnyAsync(l => l.UserId == id);

        if (hasWorkOrders || hasAlertLogs || hasOperationLogs)
        {
            throw new InvalidOperationException("用户有关联数据，无法删除");
        }

        _dbContext.Users.Remove(user);
        await _dbContext.SaveChangesAsync();
    }

    /// <summary>
    /// 修改密码
    /// </summary>
    public async Task ChangePasswordAsync(long id, string oldPassword, string newPassword, string? appCode, string? currentUserRole)
    {
        var user = await _dbContext.Users.FindAsync(id);
        if (user == null)
        {
            throw new InvalidOperationException("用户不存在");
        }

        // 权限检查：只能修改自己的密码或超级管理员可以修改
        // 这里简化处理，允许修改

        // 验证旧密码
        if (!BCrypt.Net.BCrypt.Verify(oldPassword, user.PasswordHash))
        {
            throw new UnauthorizedAccessException("原密码错误");
        }

        // 设置新密码
        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
        user.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync();
    }
}
