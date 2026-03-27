using IoTPlatform.Data;
using IoTPlatform.DTOs.Requests;
using IoTPlatform.DTOs.Responses;
using IoTPlatform.Models;
using Microsoft.EntityFrameworkCore;

namespace IoTPlatform.Services;

/// <summary>
/// 区域服务实现
/// </summary>
public class AreaService : IAreaService
{
    private readonly AppDbContext _dbContext;

    public AreaService(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>
    /// 获取区域列表
    /// </summary>
    public async Task<PagedResponse<AreaDto>> GetAreasAsync(int page, int pageSize, string? keyword, string? appCode, string? currentUserRole, List<long>? allowedAreaIds)
    {
        var query = _dbContext.Areas
            .Include(a => a.Parent)
            .Include(a => a.Customer)
            .AsQueryable();

        // 超级管理员可以查看所有区域，其他角色只能查看所属租户的区域
        if (currentUserRole != Configuration.Roles.SUPER_ADMIN && !string.IsNullOrEmpty(appCode))
        {
            query = query.Where(a => a.AppCode == appCode);
        }

        // 区域权限过滤
        if (allowedAreaIds != null && allowedAreaIds.Count > 0)
        {
            query = query.Where(a => allowedAreaIds.Contains(a.Id));
        }

        // 关键词搜索
        if (!string.IsNullOrEmpty(keyword))
        {
            query = query.Where(a =>
                a.Name.Contains(keyword) ||
                (a.Description != null && a.Description.Contains(keyword)));
        }

        var totalCount = await query.CountAsync();
        var areas = await query
            .OrderBy(a => a.SortOrder)
            .ThenByDescending(a => a.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(a => new AreaDto
            {
                Id = a.Id,
                Name = a.Name,
                Type = a.Type,
                Image = a.Image,
                ParentId = a.ParentId,
                ParentName = a.Parent != null ? a.Parent.Name : null,
                CustomerId = a.CustomerId,
                CustomerName = a.Customer != null ? a.Customer.Name : null,
                AppCode = a.AppCode,
                Description = a.Description,
                DeviceCount = a.DeviceCount,
                SortOrder = a.SortOrder,
                CreatedAt = a.CreatedAt,
                UpdatedAt = a.UpdatedAt
            })
            .ToListAsync();

        return PagedResponse<AreaDto>.Create(areas, totalCount, page, pageSize);
    }

    /// <summary>
    /// 获取区域详情
    /// </summary>
    public async Task<AreaDto?> GetAreaAsync(long id, string? appCode, string? currentUserRole, List<long>? allowedAreaIds)
    {
        var query = _dbContext.Areas
            .Include(a => a.Parent)
            .Include(a => a.Customer)
            .AsQueryable();

        // 权限过滤
        if (currentUserRole != Configuration.Roles.SUPER_ADMIN && !string.IsNullOrEmpty(appCode))
        {
            query = query.Where(a => a.AppCode == appCode);
        }

        // 区域权限过滤
        if (allowedAreaIds != null && allowedAreaIds.Count > 0)
        {
            query = query.Where(a => allowedAreaIds.Contains(a.Id));
        }

        var area = await query.FirstOrDefaultAsync(a => a.Id == id);
        if (area == null) return null;

        return new AreaDto
        {
            Id = area.Id,
            Name = area.Name,
            Type = area.Type,
            Image = area.Image,
            ParentId = area.ParentId,
            ParentName = area.Parent?.Name,
            CustomerId = area.CustomerId,
            CustomerName = area.Customer?.Name,
            AppCode = area.AppCode,
            Description = area.Description,
            DeviceCount = area.DeviceCount,
            SortOrder = area.SortOrder,
            CreatedAt = area.CreatedAt,
            UpdatedAt = area.UpdatedAt
        };
    }

    /// <summary>
    /// 创建区域
    /// </summary>
    public async Task<AreaDto> CreateAreaAsync(CreateAreaRequest request)
    {
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

        // 验证父区域是否存在
        Area? parentArea = null;
        if (request.ParentId.HasValue)
        {
            parentArea = await _dbContext.Areas.FindAsync(request.ParentId.Value);
            if (parentArea == null)
            {
                throw new InvalidOperationException("父区域不存在");
            }
        }

        var area = new Area
        {
            Name = request.Name,
            Type = request.Type,
            Image = request.Image,
            ParentId = request.ParentId,
            CustomerId = request.CustomerId,
            AppCode = customer?.AppCode,
            Description = request.Description,
            DeviceCount = 0,
            SortOrder = request.SortOrder,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _dbContext.Areas.Add(area);
        await _dbContext.SaveChangesAsync();

        // 更新父区域的设备计数（如有需要）

        return new AreaDto
        {
            Id = area.Id,
            Name = area.Name,
            Type = area.Type,
            Image = area.Image,
            ParentId = area.ParentId,
            ParentName = parentArea?.Name,
            CustomerId = area.CustomerId,
            CustomerName = customer?.Name,
            AppCode = area.AppCode,
            Description = area.Description,
            DeviceCount = area.DeviceCount,
            SortOrder = area.SortOrder,
            CreatedAt = area.CreatedAt,
            UpdatedAt = area.UpdatedAt
        };
    }

    /// <summary>
    /// 更新区域
    /// </summary>
    public async Task<AreaDto> UpdateAreaAsync(long id, UpdateAreaRequest request, string? appCode, string? currentUserRole)
    {
        var area = await _dbContext.Areas
            .Include(a => a.Parent)
            .Include(a => a.Customer)
            .FirstOrDefaultAsync(a => a.Id == id);
        if (area == null)
        {
            throw new InvalidOperationException("区域不存在");
        }

        // 权限检查
        if (currentUserRole != Configuration.Roles.SUPER_ADMIN && area.AppCode != appCode)
        {
            throw new UnauthorizedAccessException("无权修改该区域");
        }

        area.Name = request.Name;
        area.Image = request.Image;
        area.Description = request.Description;
        area.SortOrder = request.SortOrder;
        area.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync();

        return new AreaDto
        {
            Id = area.Id,
            Name = area.Name,
            Type = area.Type,
            Image = area.Image,
            ParentId = area.ParentId,
            ParentName = area.Parent?.Name,
            CustomerId = area.CustomerId,
            CustomerName = area.Customer?.Name,
            AppCode = area.AppCode,
            Description = area.Description,
            DeviceCount = area.DeviceCount,
            SortOrder = area.SortOrder,
            CreatedAt = area.CreatedAt,
            UpdatedAt = area.UpdatedAt
        };
    }

    /// <summary>
    /// 删除区域
    /// </summary>
    public async Task DeleteAreaAsync(long id, string? appCode, string? currentUserRole)
    {
        var area = await _dbContext.Areas.FindAsync(id);
        if (area == null)
        {
            throw new InvalidOperationException("区域不存在");
        }

        // 权限检查
        if (currentUserRole != Configuration.Roles.SUPER_ADMIN && area.AppCode != appCode)
        {
            throw new UnauthorizedAccessException("无权删除该区域");
        }

        // 检查是否有子区域
        var hasChildren = await _dbContext.Areas.AnyAsync(a => a.ParentId == id);
        if (hasChildren)
        {
            throw new InvalidOperationException("区域包含子区域，无法删除");
        }

        // 检查是否有关联设备
        var hasDevices = await _dbContext.AreaDevices.AnyAsync(ad => ad.AreaId == id);
        if (hasDevices)
        {
            throw new InvalidOperationException("区域包含设备，无法删除");
        }

        _dbContext.Areas.Remove(area);
        await _dbContext.SaveChangesAsync();
    }

    /// <summary>
    /// 获取区域树
    /// </summary>
    public async Task<List<AreaTreeNodeDto>> GetAreaTreeAsync(string? appCode, string? currentUserRole, List<long>? allowedAreaIds)
    {
        var query = _dbContext.Areas.AsQueryable();

        // 权限过滤
        if (currentUserRole != Configuration.Roles.SUPER_ADMIN && !string.IsNullOrEmpty(appCode))
        {
            query = query.Where(a => a.AppCode == appCode);
        }

        // 区域权限过滤
        if (allowedAreaIds != null && allowedAreaIds.Count > 0)
        {
            query = query.Where(a => allowedAreaIds.Contains(a.Id));
        }

        var allAreas = await query
            .OrderBy(a => a.SortOrder)
            .Select(a => new AreaTreeNodeDto
            {
                Id = a.Id,
                Name = a.Name,
                Type = a.Type,
                ParentId = a.ParentId,
                DeviceCount = a.DeviceCount
            })
            .ToListAsync();

        // 构建树形结构
        var tree = BuildTree(allAreas, null);
        return tree;
    }

    /// <summary>
    /// 构建区域树
    /// </summary>
    private List<AreaTreeNodeDto> BuildTree(List<AreaTreeNodeDto> allAreas, long? parentId)
    {
        return allAreas
            .Where(a => a.ParentId == parentId)
            .Select(a => new AreaTreeNodeDto
            {
                Id = a.Id,
                Name = a.Name,
                Type = a.Type,
                ParentId = a.ParentId,
                DeviceCount = a.DeviceCount,
                Children = BuildTree(allAreas, a.Id)
            })
            .ToList();
    }

    /// <summary>
    /// 获取子区域列表
    /// </summary>
    public async Task<List<AreaDto>> GetChildAreasAsync(long parentId, string? appCode, List<long>? allowedAreaIds)
    {
        var query = _dbContext.Areas
            .Where(a => a.ParentId == parentId);

        // 租户过滤
        if (!string.IsNullOrEmpty(appCode))
        {
            query = query.Where(a => a.AppCode == appCode);
        }

        // 区域权限过滤
        if (allowedAreaIds != null && allowedAreaIds.Count > 0)
        {
            query = query.Where(a => allowedAreaIds.Contains(a.Id));
        }

        return await query
            .OrderBy(a => a.SortOrder)
            .Select(a => new AreaDto
            {
                Id = a.Id,
                Name = a.Name,
                Type = a.Type,
                Image = a.Image,
                ParentId = a.ParentId,
                AppCode = a.AppCode,
                Description = a.Description,
                DeviceCount = a.DeviceCount,
                SortOrder = a.SortOrder,
                CreatedAt = a.CreatedAt,
                UpdatedAt = a.UpdatedAt
            })
            .ToListAsync();
    }
}
