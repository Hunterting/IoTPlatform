using IoTPlatform.Data.Repositories.Interfaces;
using IoTPlatform.DTOs.Requests;
using IoTPlatform.DTOs.Responses;
using IoTPlatform.Helpers;
using IoTPlatform.Models;
using IoTPlatform.Services.Guards;
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

    /// <summary>获取区域列表（原签名保留，下级零回归）。</summary>
    public Task<PagedResponse<AreaDto>> GetAreasAsync(int page, int pageSize, string? keyword, string? appCode, string? currentUserRole, List<long>? allowedAreaIds)
        => GetAreasAsync(page, pageSize, keyword, appCode, currentUserRole, allowedAreaIds, null);

    /// <summary>
    /// 【多客户授权 T04】按授权客户集合取区域列表；customerIds 为 null 时逐字走原路径。
    /// </summary>
    public async Task<PagedResponse<AreaDto>> GetAreasAsync(int page, int pageSize, string? keyword, string? appCode, string? currentUserRole, List<long>? allowedAreaIds, IReadOnlyCollection<long>? customerIds)
    {
        var queryAppCode = (currentUserRole != Configuration.Roles.SUPER_ADMIN && !string.IsNullOrEmpty(appCode)) ? appCode : null;
        var query = ApplyCustomerIds(_areaRepository.GetQueryable(queryAppCode, allowedAreaIds), customerIds);

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

    /// <summary>获取区域详情（原签名保留，下级零回归）。</summary>
    public Task<AreaDto?> GetAreaAsync(long id, string? appCode, string? currentUserRole, List<long>? allowedAreaIds)
        => GetAreaAsync(id, appCode, currentUserRole, allowedAreaIds, null);

    /// <summary>【多客户授权 T04】区域详情必须在授权客户集合内，否则按不存在处理。</summary>
    public async Task<AreaDto?> GetAreaAsync(long id, string? appCode, string? currentUserRole, List<long>? allowedAreaIds, IReadOnlyCollection<long>? customerIds)
    {
        var queryAppCode = (currentUserRole != Configuration.Roles.SUPER_ADMIN && !string.IsNullOrEmpty(appCode)) ? appCode : null;
        var area = await _areaRepository.GetByIdAsync(id, appCode: queryAppCode, allowedAreaIds: allowedAreaIds);
        if (area == null) return null;

        if (customerIds != null && (!area.CustomerId.HasValue || !customerIds.Contains(area.CustomerId.Value)))
        {
            return null;
        }

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
    /// 创建区域（旧签名保留：内部调用零回归）。
    /// </summary>
    public Task<AreaDto> CreateAreaAsync(CreateAreaRequest request)
        => CreateAreaAsync(request, null, customerScopeVerified: true);

    /// <summary>
    /// M1 二次防御重载：Controller 已校验目标客户在授权集合内，appCode 由服务端派生。
    /// </summary>
    public async Task<AreaDto> CreateAreaAsync(
        CreateAreaRequest request,
        string? appCode,
        bool customerScopeVerified)
    {
        if (!customerScopeVerified)
        {
            throw new UnauthorizedAccessException("无权在该客户下创建区域");
        }

        // 服务端派生的 AppCode 与客户权威 AppCode 必须一致，杜绝 request.AppCode 越权落库。
        if (request.CustomerId.HasValue && !string.IsNullOrEmpty(appCode))
        {
            request.AppCode = appCode;
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
    public Task<AreaDto> UpdateAreaAsync(long id, UpdateAreaRequest request, string? appCode, string? currentUserRole)
        => UpdateAreaCoreAsync(id, request, appCode, currentUserRole, null, false);

    /// <summary>
    /// B5/M8 三分支改删重载：范围由 Controller 显式解析后下推；未校验通过一律 403。
    /// </summary>
    public Task<AreaDto> UpdateAreaAsync(
        long id, UpdateAreaRequest request, string? appCode, CustomerScope scope, bool customerScopeVerified)
    {
        if (scope == null || !customerScopeVerified)
        {
            return Task.FromException<AreaDto>(
                new ForbiddenAccessException("区域更新未通过客户范围校验"));
        }

        return UpdateAreaCoreAsync(id, request, appCode, null, scope, true);
    }

    private async Task<AreaDto> UpdateAreaCoreAsync(
        long id,
        UpdateAreaRequest request,
        string? appCode,
        string? currentUserRole,
        CustomerScope? scope,
        bool customerScopeVerified)
    {
        if (scope != null && !customerScopeVerified)
        {
            throw new ForbiddenAccessException("区域更新未通过客户范围校验");
        }

        var area = await _areaRepository.GetByIdAsync(id);
        if (area == null)
        {
            throw new InvalidOperationException("区域不存在");
        }

        // B5/M8：All 放行 / Multi 比 DB 授权集合 / Single 沿用 claim 单值；写库之前完成。
        WriteScopeAssertion.EnsureCanWrite(
            scope, area.CustomerId, area.AppCode, appCode, currentUserRole, "修改该区域");

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
    public Task DeleteAreaAsync(long id, string? appCode, string? currentUserRole)
        => DeleteAreaCoreAsync(id, appCode, currentUserRole, null, false);

    /// <summary>B5/M8 三分支删除重载：范围由 Controller 显式解析后下推；未校验通过一律 403。</summary>
    public Task DeleteAreaAsync(long id, string? appCode, CustomerScope scope, bool customerScopeVerified)
    {
        if (scope == null || !customerScopeVerified)
        {
            return Task.FromException(new ForbiddenAccessException("区域删除未通过客户范围校验"));
        }

        return DeleteAreaCoreAsync(id, appCode, null, scope, true);
    }

    private async Task DeleteAreaCoreAsync(
        long id,
        string? appCode,
        string? currentUserRole,
        CustomerScope? scope,
        bool customerScopeVerified)
    {
        if (scope != null && !customerScopeVerified)
        {
            throw new ForbiddenAccessException("区域删除未通过客户范围校验");
        }

        var area = await _areaRepository.GetByIdAsync(id);
        if (area == null)
        {
            throw new InvalidOperationException("区域不存在");
        }

        // B5/M8：写库/子区域检查之前完成三分支归属判定。
        WriteScopeAssertion.EnsureCanWrite(
            scope, area.CustomerId, area.AppCode, appCode, currentUserRole, "删除该区域");

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

    /// <summary>获取区域树（原签名保留，下级零回归）。</summary>
    public Task<List<AreaTreeNodeDto>> GetAreaTreeAsync(string? appCode, string? currentUserRole, List<long>? allowedAreaIds)
        => GetAreaTreeAsync(appCode, currentUserRole, allowedAreaIds, null);

    /// <summary>
    /// 【多客户授权 T04】多客户区域树：一次 SQL 取回全部授权客户的区域节点再在内存构树，禁止 N+1。
    /// </summary>
    public async Task<List<AreaTreeNodeDto>> GetAreaTreeAsync(string? appCode, string? currentUserRole, List<long>? allowedAreaIds, IReadOnlyCollection<long>? customerIds)
    {
        var queryAppCode = (currentUserRole != Configuration.Roles.SUPER_ADMIN && !string.IsNullOrEmpty(appCode)) ? appCode : null;

        if (customerIds != null)
        {
            var ids = customerIds.Where(id => id > 0).Distinct().ToList();
            if (ids.Count == 0)
            {
                return new List<AreaTreeNodeDto>();
            }

            // 一次 SQL：Contains 翻译为 IN，取回含子节点的全部区域（原单客户重载语义不变）。
            var areas = await _areaRepository.GetTreeAsync(ids, queryAppCode);
            var multiAreas = areas
                .OrderBy(a => a.SortOrder)
                .Select(a => new AreaTreeNodeDto
                {
                    Id = a.Id,
                    Name = a.Name,
                    Type = a.Type,
                    ParentId = a.ParentId,
                    DeviceCount = a.DeviceCount,
                    AppCode = a.AppCode,
                    CustomerId = a.CustomerId
                })
                .ToList();

            return BuildTree(multiAreas, null);
        }

        var query = _areaRepository.GetQueryable(queryAppCode, allowedAreaIds);

        var allAreas = await query
            .OrderBy(a => a.SortOrder)
            .Select(a => new AreaTreeNodeDto
            {
                Id = a.Id,
                Name = a.Name,
                Type = a.Type,
                ParentId = a.ParentId,
                DeviceCount = a.DeviceCount,
                AppCode = a.AppCode,
                CustomerId = a.CustomerId
            })
            .ToListAsync();

        // 构建树形结构
        var tree = BuildTree(allAreas, null);
        return tree;
    }

    /// <summary>
    /// 将授权客户集合过滤附加到区域查询：物化为 List 供 EF Core 稳定翻译为 SQL IN；
    /// 空集合返回空结果（Multi 空集合是"无权限"，不是"不过滤"）。
    /// </summary>
    private static IQueryable<Area> ApplyCustomerIds(IQueryable<Area> query, IReadOnlyCollection<long>? customerIds)
    {
        if (customerIds == null)
        {
            return query;
        }

        var ids = customerIds.Where(id => id > 0).Distinct().ToList();
        return ids.Count == 0
            ? query.Where(a => false)
            : query.Where(a => a.CustomerId.HasValue && ids.Contains(a.CustomerId.Value));
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
                AppCode = a.AppCode,
                CustomerId = a.CustomerId,
                Children = BuildTree(allAreas, a.Id)
            })
            .ToList();
    }

    /// <summary>获取子区域列表（原签名保留，下级零回归）。</summary>
    public Task<List<AreaDto>> GetChildAreasAsync(long parentId, string? appCode, List<long>? allowedAreaIds)
        => GetChildAreasAsync(parentId, appCode, allowedAreaIds, null);

    /// <summary>【多客户授权 T04】按授权客户集合过滤子区域。</summary>
    public async Task<List<AreaDto>> GetChildAreasAsync(long parentId, string? appCode, List<long>? allowedAreaIds, IReadOnlyCollection<long>? customerIds)
    {
        var areas = await _areaRepository.GetChildrenAsync(parentId, appCode);

        if (customerIds != null)
        {
            var ids = customerIds.Where(id => id > 0).Distinct().ToList();
            areas = ids.Count == 0
                ? Enumerable.Empty<Area>()
                : areas.Where(a => a.CustomerId.HasValue && ids.Contains(a.CustomerId.Value)).ToList();
        }

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
