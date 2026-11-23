using IoTPlatform.Data.Repositories.Interfaces;
using IoTPlatform.DTOs.Requests;
using IoTPlatform.DTOs.Responses;
using IoTPlatform.Helpers;
using IoTPlatform.Models;
using IoTPlatform.Services.Guards;
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

    /// <summary>旧签名保留：委托到范围下推重载，旧参数组合（下级/内部）零回归。</summary>
    public Task<PagedResponse<UserDto>> GetUsersAsync(
        int page, int pageSize, string? keyword, string? appCode, string? currentUserRole)
        => GetUsersAsync(page, pageSize, keyword, appCode, currentUserRole, null, null);

    /// <summary>
    /// B4/M5：列表范围下推。admin 的 appCode=null 不再等于零过滤——授权客户集合在此真正生效。
    /// </summary>
    public async Task<PagedResponse<UserDto>> GetUsersAsync(
        int page, int pageSize, string? keyword, string? appCode, string? currentUserRole,
        IReadOnlyCollection<long>? customerIds, IReadOnlyCollection<string>? appCodes = null)
    {
        // 确定查询的appCode
        var queryAppCode = appCode;
        if (currentUserRole != Configuration.Roles.SUPER_ADMIN && string.IsNullOrEmpty(appCode))
        {
            queryAppCode = null;
        }

        // 构建查询条件
        var query = _userRepository.GetQueryable(queryAppCode, null, customerIds, appCodes);

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
            Customer? customer = null;
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
                IsSuperAdmin = user.IsSuperAdmin,
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
    /// 创建用户（原签名保留：内部调用与 T03 AuthService 兼容，零回归）。
    /// </summary>
    public Task<UserDto> CreateUserAsync(CreateUserRequest request)
        => CreateUserAsync(request, null, true);

    /// <summary>
    /// 【多客户授权 T04】承载发起人 ID 与目标客户授权校验结果的建户重载。
    ///
    /// <para>真正的越权判定由控制器通过 ICustomerScopeService 完成（DB 权威）；此处对
    /// <paramref name="customerScopeVerified"/> 做二次防御，未校验通过一律拒绝入库。</para>
    /// <para><b>零回归铁律</b>：AppCode 派生逻辑 <c>customer?.AppCode</c> 逐字保留，绝不顺带写 user_customers（D9）。</para>
    /// </summary>
    /// <param name="request">建户请求。</param>
    /// <param name="actingUserId">发起人 ID（M10 审计预留）。</param>
    /// <param name="customerScopeVerified">目标客户已通过范围服务校验。</param>
    public async Task<UserDto> CreateUserAsync(CreateUserRequest request, long? actingUserId, bool customerScopeVerified)
    {
        if (!customerScopeVerified)
        {
            throw new UnauthorizedAccessException("无权在该客户下创建用户");
        }
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
    public Task<UserDto> UpdateUserAsync(long id, UpdateUserRequest request, string? appCode, string? currentUserRole)
        => UpdateUserCoreAsync(id, request, appCode, currentUserRole, null, false);

    /// <summary>B5/M8 三分支改删重载：范围由 Controller 显式解析后下推；未校验通过一律 403。</summary>
    public Task<UserDto> UpdateUserAsync(
        long id, UpdateUserRequest request, string? appCode, CustomerScope scope, bool customerScopeVerified)
    {
        if (scope == null || !customerScopeVerified)
        {
            return Task.FromException<UserDto>(
                new ForbiddenAccessException("用户更新未通过客户范围校验"));
        }

        return UpdateUserCoreAsync(id, request, appCode, null, scope, true);
    }

    private async Task<UserDto> UpdateUserCoreAsync(
        long id,
        UpdateUserRequest request,
        string? appCode,
        string? currentUserRole,
        CustomerScope? scope,
        bool customerScopeVerified)
    {
        if (scope != null && !customerScopeVerified)
        {
            throw new ForbiddenAccessException("用户更新未通过客户范围校验");
        }

        var user = await _userRepository.GetByIdAsync(id);
        if (user == null)
        {
            throw new InvalidOperationException("用户不存在");
        }

        // B5/M8：All 放行 / Multi 比 DB 授权集合 / Single 沿用 claim 单值；写库之前完成。
        WriteScopeAssertion.EnsureCanWrite(
            scope, user.CustomerId, user.AppCode, appCode, currentUserRole, "修改该用户");

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
    public Task DeleteUserAsync(long id, string? appCode, string? currentUserRole)
        => DeleteUserCoreAsync(id, appCode, currentUserRole, null, false);

    /// <summary>B5/M8 三分支删除重载：范围由 Controller 显式解析后下推；未校验通过一律 403。</summary>
    public Task DeleteUserAsync(long id, string? appCode, CustomerScope scope, bool customerScopeVerified)
    {
        if (scope == null || !customerScopeVerified)
        {
            return Task.FromException(new ForbiddenAccessException("用户删除未通过客户范围校验"));
        }

        return DeleteUserCoreAsync(id, appCode, null, scope, true);
    }

    private async Task DeleteUserCoreAsync(
        long id,
        string? appCode,
        string? currentUserRole,
        CustomerScope? scope,
        bool customerScopeVerified)
    {
        if (scope != null && !customerScopeVerified)
        {
            throw new ForbiddenAccessException("用户删除未通过客户范围校验");
        }

        var user = await _userRepository.GetByIdAsync(id);
        if (user == null)
        {
            throw new InvalidOperationException("用户不存在");
        }

        // B5/M8：任何关联数据检查与写库之前完成归属判定。
        WriteScopeAssertion.EnsureCanWrite(
            scope, user.CustomerId, user.AppCode, appCode, currentUserRole, "删除该用户");

        // 检查是否有关联数据（工单、操作日志等）
        var hasWorkOrders = await _workOrderRepository.ExistsAsync(w => w.CreatedBy == user.Email);
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
