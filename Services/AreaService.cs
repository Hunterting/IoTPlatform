using IoTPlatform.Data.Repositories.Interfaces;
using IoTPlatform.DTOs.Requests;
using IoTPlatform.DTOs.Responses;
using IoTPlatform.Helpers;
using IoTPlatform.Models;
using Microsoft.EntityFrameworkCore;

namespace IoTPlatform.Services;

/// <summary>
/// 区域服务实现（使用仓储模式）
/// </summary>
public class AreaService : IAreaService
{
    private readonly IAreaRepository _areaRepository;
    private readonly ICustomerRepository _customerRepository;
    private readonly IDeviceRepository _deviceRepository;
    private readonly IUnitOfWork _unitOfWork;

    public AreaService(
        IAreaRepository areaRepository,
        ICustomerRepository customerRepository,
        IDeviceRepository deviceRepository,
        IUnitOfWork unitOfWork)
    {
        _areaRepository = areaRepository;
        _customerRepository = customerRepository;
        _deviceRepository = deviceRepository;
        _unitOfWork = unitOfWork;
    }

    /// <summary>
    /// 获取区域列表
    /// </summary>
    public async Task<PagedResponse<AreaDto>> GetAreasAsync(int page, int pageSize, string? keyword, string? appCode, string? currentUserRole, List<long>? allowedAreaIds)
    {
        var queryAppCode = (currentUserRole != Configuration.Roles.SUPER_ADMIN && !string.IsNullOrEmpty(appCode)) ? appCode : null;
        var query = _areaRepository.GetQueryable(queryAppCode, allowedAreaIds);

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
            .ToListAsync();

        // 转换为DTO
        var areaDtos = new List<AreaDto>();
        foreach (var area in areas)
        {
            Area? parent = null;
            Customer? customer = null;
            if (area.ParentId.HasValue)
            {
                parent = await _areaRepository.GetByIdAsync(area.ParentId.Value);
            }
            if (area.CustomerId.HasValue)
            {
                customer = await _customerRepository.GetByIdAsync(area.CustomerId.Value);
            }

            var areaDto = new AreaDto
            {
                Id = area.Id,
                Name = area.Name,
                Type = area.Type,
                Image = area.Image,
                ParentId = area.ParentId,
                ParentName = parent?.Name,
                CustomerId = area.CustomerId,
                CustomerName = customer?.Name,
                AppCode = area.AppCode,
                Description = area.Description,
                DeviceCount = area.DeviceCount,
                SortOrder = area.SortOrder,
                CreatedAt = area.CreatedAt,
                UpdatedAt = area.UpdatedAt
            };
            areaDtos.Add(areaDto);
        }

        return PagedResponse<AreaDto>.Create(areaDtos, totalCount, page, pageSize);
    }

    /// <summary>
    /// 获取区域详情
    /// </summary>
    public async Task<AreaDto?> GetAreaAsync(long id, string? appCode, string? currentUserRole, List<long>? allowedAreaIds)
    {
        var queryAppCode = (currentUserRole != Configuration.Roles.SUPER_ADMIN && !string.IsNullOrEmpty(appCode)) ? appCode : null;
        var area = await _areaRepository.GetByIdAsync(id, appCode: queryAppCode, allowedAreaIds: allowedAreaIds);
        if (area == null) return null;

        Area? parent = null;
        Customer? customer = null;
        if (area.ParentId.HasValue)
        {
            parent = await _areaRepository.GetByIdAsync(area.ParentId.Value);
        }
        if (area.CustomerId.HasValue)
        {
            customer = await _customerRepository.GetByIdAsync(area.CustomerId.Value);
        }

        return new AreaDto
        {
            Id = area.Id,
            Name = area.Name,
            Type = area.Type,
            Image = area.Image,
            ParentId = area.ParentId,
            ParentName = parent?.Name,
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
    /// 创建区域
    /// </summary>
    public async Task<AreaDto> CreateAreaAsync(CreateAreaRequest request)
    {
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

        // 验证父区域是否存在
        Area? parentArea = null;
        if (request.ParentId.HasValue)
        {
            parentArea = await _areaRepository.GetByIdAsync(request.ParentId.Value);
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

        await _areaRepository.AddAsync(area);
        await _unitOfWork.SaveChangesAsync();

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
        var area = await _areaRepository.GetByIdAsync(id);
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

        await _areaRepository.UpdateAsync(area);
        await _unitOfWork.SaveChangesAsync();

        // 获取关联数据
        Area? parent = null;
        Customer? customer = null;
        if (area.ParentId.HasValue)
        {
            parent = await _areaRepository.GetByIdAsync(area.ParentId.Value);
        }
        if (area.CustomerId.HasValue)
        {
            customer = await _customerRepository.GetByIdAsync(area.CustomerId.Value);
        }

        return new AreaDto
        {
            Id = area.Id,
            Name = area.Name,
            Type = area.Type,
            Image = area.Image,
            ParentId = area.ParentId,
            ParentName = parent?.Name,
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
    /// 删除区域
    /// </summary>
    public async Task DeleteAreaAsync(long id, string? appCode, string? currentUserRole)
    {
        var area = await _areaRepository.GetByIdAsync(id);
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
        var hasChildren = await _areaRepository.ExistsAsync(a => a.ParentId == id);
        if (hasChildren)
        {
            throw new InvalidOperationException("区域包含子区域，无法删除");
        }

        // 检查是否有关联设备
        var hasDevices = await _deviceRepository.ExistsAsync(d => d.AreaId == id);
        if (hasDevices)
        {
            throw new InvalidOperationException("区域包含设备，无法删除");
        }

        await _areaRepository.DeleteAsync(area);
        await _unitOfWork.SaveChangesAsync();
    }

    /// <summary>
    /// 获取区域树
    /// </summary>
    public async Task<List<AreaTreeNodeDto>> GetAreaTreeAsync(string? appCode, string? currentUserRole, List<long>? allowedAreaIds)
    {
        var queryAppCode = (currentUserRole != Configuration.Roles.SUPER_ADMIN && !string.IsNullOrEmpty(appCode)) ? appCode : null;
        var query = _areaRepository.GetQueryable(queryAppCode, allowedAreaIds);

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
        var areas = await _areaRepository.GetChildrenAsync(parentId, appCode);

        // 区域权限过滤
        var filteredAreas = areas.ToList();
        if (allowedAreaIds != null && allowedAreaIds.Count > 0)
        {
            filteredAreas = areas.Where(a => allowedAreaIds.Contains(a.Id)).ToList();
        }

        return filteredAreas.Select(a => new AreaDto
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
        }).ToList();
    }
}
