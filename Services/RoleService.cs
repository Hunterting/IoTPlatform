using IoTPlatform.Data.Repositories.Interfaces;
using IoTPlatform.DTOs.Requests;
using IoTPlatform.DTOs.Responses;
using IoTPlatform.Models;
using System.Text.Json;

namespace IoTPlatform.Services;

/// <summary>
/// 角色服务实现（使用仓储模式）
/// </summary>
public class RoleService : IRoleService
{
    private readonly IRoleRepository _roleRepository;
    private readonly IUserRepository _userRepository;
    private readonly IUnitOfWork _unitOfWork;

    public RoleService(
        IRoleRepository roleRepository,
        IUserRepository userRepository,
        IUnitOfWork unitOfWork)
    {
        _roleRepository = roleRepository;
        _userRepository = userRepository;
        _unitOfWork = unitOfWork;
    }

    /// <summary>
    /// 获取角色列表
    /// </summary>
    public async Task<PagedResponse<RoleDto>> GetRolesAsync(int page, int pageSize, string? keyword, string? appCode, string? currentUserRole)
    {
        // 超级管理员可以查看所有角色，其他角色只能查看所属租户的角色
        var queryAppCode = (currentUserRole != Configuration.Roles.SUPER_ADMIN && !string.IsNullOrEmpty(appCode)) ? appCode : null;

        var query = _roleRepository.GetQueryable(queryAppCode);

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
    /// 创建角色
    /// </summary>
    public async Task<RoleDto> CreateRoleAsync(CreateRoleRequest request)
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
            AppCode = null, // 新角色默认为系统级，可根据需求调整
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
    public async Task<RoleDto> UpdateRoleAsync(long id, UpdateRoleRequest request, string? appCode, string? currentUserRole)
    {
        var role = await _roleRepository.GetByIdAsync(id);
        if (role == null)
        {
            throw new InvalidOperationException("角色不存在");
        }

        // 权限检查
        if (currentUserRole != Configuration.Roles.SUPER_ADMIN && role.AppCode != appCode)
        {
            throw new UnauthorizedAccessException("无权修改该角色");
        }

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
    public async Task DeleteRoleAsync(long id, string? appCode, string? currentUserRole)
    {
        var role = await _roleRepository.GetByIdAsync(id);
        if (role == null)
        {
            throw new InvalidOperationException("角色不存在");
        }

        // 权限检查
        if (currentUserRole != Configuration.Roles.SUPER_ADMIN && role.AppCode != appCode)
        {
            throw new UnauthorizedAccessException("无权删除该角色");
        }

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
