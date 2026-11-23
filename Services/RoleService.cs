using IoTPlatform.Data.Repositories.Interfaces;
using IoTPlatform.DTOs.Requests;
using IoTPlatform.DTOs.Responses;
using IoTPlatform.Helpers;
using IoTPlatform.Models;
using IoTPlatform.Services.Guards;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace IoTPlatform.Services;

/// <summary>
/// 角色服务实现（使用仓储模式）
/// </summary>
public class RoleService : IRoleService
{
    private readonly IRoleRepository _roleRepository;
    private readonly IUserRepository _userRepository;
    private readonly ICustomerRepository _customerRepository;
    private readonly IUnitOfWork _unitOfWork;

    public RoleService(
        IRoleRepository roleRepository,
        IUserRepository userRepository,
        ICustomerRepository customerRepository,
        IUnitOfWork unitOfWork)
    {
        _roleRepository = roleRepository;
        _userRepository = userRepository;
        _customerRepository = customerRepository;
        _unitOfWork = unitOfWork;
    }

    /// <summary>
    /// 获取角色列表
    /// </summary>
        /// <summary>旧签名保留：委托到范围下推重载，下级/内部路径零回归。</summary>
    public Task<PagedResponse<RoleDto>> GetRolesAsync(int page, int pageSize, string? keyword, string? appCode, string? currentUserRole) => GetRolesAsync(page, pageSize, keyword, appCode, currentUserRole, null, null);

    /// <summary>B4/M5：admin 的 appCode=null 不再等于零过滤。</summary>
    public async Task<PagedResponse<RoleDto>> GetRolesAsync(int page, int pageSize, string? keyword, string? appCode, string? currentUserRole, IReadOnlyCollection<long>? customerIds, IReadOnlyCollection<string>? appCodes = null)
    {
        // 超级管理员可以查看所有角色，其他角色只能查看所属租户的角色
        var queryAppCode = (currentUserRole != Configuration.Roles.SUPER_ADMIN && !string.IsNullOrEmpty(appCode)) ? appCode : null;

        var query = _roleRepository.GetQueryable(queryAppCode);
        // B4 QA-D2：平台级角色（AppCode="system"）对所有角色可读；越集私有角色仍被过滤。
        query = query.ApplyQueryScopeWithShared(appCode, customerIds, appCodes);

        // 关键词搜索
        if (!string.IsNullOrEmpty(keyword))
        {
            query = query.Where(r =>
                r.Name.Contains(keyword) ||
                r.Code.Contains(keyword));
        }

        var totalCount = await query.CountAsync();
        var roles = await query
            .OrderByDescending(r => r.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        // 转换为DTO
        var roleDtos = roles.Select(r => new RoleDto
        {
            Id = r.Id,
            Code = r.Code,
            Name = r.Name,
            Description = r.Description,
            Permissions = r.Permissions,
            PermissionList = ParsePermissions(r.Permissions),
            DataScope = r.DataScope,
            AppCode = r.AppCode,
            IsSystem = r.IsSystem,
            CreatedAt = r.CreatedAt,
            UpdatedAt = r.UpdatedAt
        }).ToList();

        return PagedResponse<RoleDto>.Create(roleDtos, totalCount, page, pageSize);
    }

    /// <summary>
    /// 获取角色详情
    /// </summary>
    public async Task<RoleDto?> GetRoleAsync(long id, string? appCode, string? currentUserRole)
    {
        var queryAppCode = (currentUserRole != Configuration.Roles.SUPER_ADMIN && !string.IsNullOrEmpty(appCode)) ? appCode : null;

        var role = await _roleRepository.GetByIdAsync(id, appCode: queryAppCode);
        if (role == null) return null;

        return new RoleDto
        {
            Id = role.Id,
            Code = role.Code,
            Name = role.Name,
            Description = role.Description,
            Permissions = role.Permissions,
            PermissionList = ParsePermissions(role.Permissions),
            DataScope = role.DataScope,
            AppCode = role.AppCode,
            IsSystem = role.IsSystem,
            CreatedAt = role.CreatedAt,
            UpdatedAt = role.UpdatedAt
        };
    }

    /// <summary>
    /// 创建角色（旧签名保留：超管/下级原路径逐字不改）。
    /// </summary>
    public async Task<RoleDto> CreateRoleAsync(CreateRoleRequest request, string? appCode = null, string? currentUserRole = null)
    {
        string? resolvedAppCode = appCode;
        if (currentUserRole == Configuration.Roles.SUPER_ADMIN && request.CustomerId.HasValue)
        {
            var targetCustomer = await _customerRepository.GetByIdAsync(request.CustomerId.Value);
            resolvedAppCode = targetCustomer?.AppCode;
        }

        return await CreateRoleCoreAsync(request, resolvedAppCode).ConfigureAwait(false);
    }

    /// <summary>
    /// M10 提权面二次防御重载：仅当 Controller 已用 DB 授权集合校验目标客户、
    /// 并完成权限白名单降级时，才允许写入显式归属的角色。
    /// </summary>
    /// <param name="request">创建请求。</param>
    /// <param name="resolvedAppCode">服务端派生的客户 AppCode，严禁为 null。</param>
    /// <param name="customerScopeVerified">Controller 是否已完成归属与权限校验。</param>
    public Task<RoleDto> CreateRoleAsync(CreateRoleRequest request, string? resolvedAppCode, bool customerScopeVerified)
    {
        if (!customerScopeVerified)
        {
            return Task.FromException<RoleDto>(
                new ForbiddenAccessException("角色创建未通过客户范围校验"));
        }

        if (string.IsNullOrWhiteSpace(resolvedAppCode))
        {
            return Task.FromException<RoleDto>(
                new InvalidOperationException("目标客户缺少 AppCode，禁止创建平台级角色"));
        }

        return CreateRoleCoreAsync(request, resolvedAppCode);
    }

    /// <summary>创建角色真正写库的核心实现（所有调用方必须先完成归属判定）。</summary>
    private async Task<RoleDto> CreateRoleCoreAsync(CreateRoleRequest request, string? resolvedAppCode)
    {
        // 检查角色代码是否已存在
        var exists = await _roleRepository.CodeExistsAsync(request.Code);
        if (exists)
        {
            throw new InvalidOperationException("角色代码已存在");
        }

        // 序列化权限列表为JSON
        var permissionsJson = request.Permissions != null
            ? JsonSerializer.Serialize(request.Permissions)
            : null;

        var role = new Role
        {
            Code = request.Code,
            Name = request.Name,
            Description = request.Description,
            Permissions = permissionsJson,
            DataScope = request.DataScope,
            // 归属一律服务端派生；M10 路径已强制非 null（平台级角色只能由旧签名内部通道创建）。
            AppCode = resolvedAppCode,
            IsSystem = false,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _roleRepository.AddAsync(role);
        await _unitOfWork.SaveChangesAsync();

        return new RoleDto
        {
            Id = role.Id,
            Code = role.Code,
            Name = role.Name,
            Description = role.Description,
            Permissions = role.Permissions,
            PermissionList = ParsePermissions(role.Permissions),
            DataScope = role.DataScope,
            AppCode = role.AppCode,
            IsSystem = role.IsSystem,
            CreatedAt = role.CreatedAt,
            UpdatedAt = role.UpdatedAt
        };
    }

    /// <summary>
    /// 更新角色
    /// </summary>
    public Task<RoleDto> UpdateRoleAsync(long id, UpdateRoleRequest request, string? appCode, string? currentUserRole)
        => UpdateRoleCoreAsync(id, request, appCode, currentUserRole, null, false);

    /// <summary>
    /// B5/M8 三分支改删重载：范围由 Controller 显式解析后下推；未校验通过一律 403。
    /// </summary>
    public Task<RoleDto> UpdateRoleAsync(
        long id, UpdateRoleRequest request, string? appCode, CustomerScope scope, bool customerScopeVerified)
    {
        if (scope == null || !customerScopeVerified)
        {
            return Task.FromException<RoleDto>(
                new ForbiddenAccessException("角色更新未通过客户范围校验"));
        }

        return UpdateRoleCoreAsync(id, request, appCode, null, scope, true);
    }

    private async Task<RoleDto> UpdateRoleCoreAsync(
        long id,
        UpdateRoleRequest request,
        string? appCode,
        string? currentUserRole,
        CustomerScope? scope,
        bool customerScopeVerified)
    {
        if (scope != null && !customerScopeVerified)
        {
            throw new ForbiddenAccessException("角色更新未通过客户范围校验");
        }

        var role = await _roleRepository.GetByIdAsync(id);
        if (role == null)
        {
            throw new InvalidOperationException("角色不存在");
        }

        // B5/M8：Role 无 CustomerId 列，Multi 分支按 DB 派生的 scope.AppCodes 反查比对。
        WriteScopeAssertion.EnsureCanWrite(
            scope, null, role.AppCode, appCode, currentUserRole, "修改该角色");

        // 系统角色不能修改
        if (role.IsSystem)
        {
            throw new InvalidOperationException("系统角色不能修改");
        }

        role.Name = request.Name;
        role.Description = request.Description;
        role.DataScope = request.DataScope;
        role.UpdatedAt = DateTime.UtcNow;

        // 更新权限
        if (request.Permissions != null)
        {
            role.Permissions = JsonSerializer.Serialize(request.Permissions);
        }

        await _roleRepository.UpdateAsync(role);
        await _unitOfWork.SaveChangesAsync();

        return new RoleDto
        {
            Id = role.Id,
            Code = role.Code,
            Name = role.Name,
            Description = role.Description,
            Permissions = role.Permissions,
            PermissionList = ParsePermissions(role.Permissions),
            DataScope = role.DataScope,
            AppCode = role.AppCode,
            IsSystem = role.IsSystem,
            CreatedAt = role.CreatedAt,
            UpdatedAt = role.UpdatedAt
        };
    }

    /// <summary>
    /// 删除角色
    /// </summary>
    public Task DeleteRoleAsync(long id, string? appCode, string? currentUserRole)
        => DeleteRoleCoreAsync(id, appCode, currentUserRole, null, false);

    /// <summary>B5/M8 三分支删除重载：范围由 Controller 显式解析后下推；未校验通过一律 403。</summary>
    public Task DeleteRoleAsync(long id, string? appCode, CustomerScope scope, bool customerScopeVerified)
    {
        if (scope == null || !customerScopeVerified)
        {
            return Task.FromException(new ForbiddenAccessException("角色删除未通过客户范围校验"));
        }

        return DeleteRoleCoreAsync(id, appCode, null, scope, true);
    }

    private async Task DeleteRoleCoreAsync(
        long id,
        string? appCode,
        string? currentUserRole,
        CustomerScope? scope,
        bool customerScopeVerified)
    {
        if (scope != null && !customerScopeVerified)
        {
            throw new ForbiddenAccessException("角色删除未通过客户范围校验");
        }

        var role = await _roleRepository.GetByIdAsync(id);
        if (role == null)
        {
            throw new InvalidOperationException("角色不存在");
        }

        // B5/M8：Role 无 CustomerId 列，Multi 分支按 DB 派生的 scope.AppCodes 反查比对。
        WriteScopeAssertion.EnsureCanWrite(
            scope, null, role.AppCode, appCode, currentUserRole, "删除该角色");

        // 系统角色不能删除
        if (role.IsSystem)
        {
            throw new InvalidOperationException("系统角色不能删除");
        }

        // 检查是否有用户使用该角色
        var hasUsers = await _userRepository.ExistsAsync(u => u.Role == role.Code);
        if (hasUsers)
        {
            throw new InvalidOperationException("角色已被用户使用，无法删除");
        }

        await _roleRepository.DeleteAsync(role);
        await _unitOfWork.SaveChangesAsync();
    }

    /// <summary>
    /// 获取角色权限列表
    /// </summary>
    public async Task<List<string>> GetRolePermissionsAsync(long roleId)
    {
        var role = await _roleRepository.GetByIdAsync(roleId);
        if (role == null)
        {
            throw new InvalidOperationException("角色不存在");
        }

        // 如果是预定义角色，从RoleConfig获取权限
        var permissions = Configuration.Roles.GetRolePermissions(role.Code);
        if (permissions.Count > 0)
        {
            return permissions;
        }

        // 自定义角色，从数据库获取
        return ParsePermissions(role.Permissions) ?? new List<string>();
    }

    /// <summary>
    /// 解析权限JSON字符串
    /// </summary>
    private List<string>? ParsePermissions(string? permissionsJson)
    {
        if (string.IsNullOrEmpty(permissionsJson))
            return null;

        try
        {
            return JsonSerializer.Deserialize<List<string>>(permissionsJson);
        }
        catch
        {
            return null;
        }
    }
}
