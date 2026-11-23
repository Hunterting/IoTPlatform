using IoTPlatform.Data.Repositories.Interfaces;
using IoTPlatform.DTOs.Requests;
using IoTPlatform.DTOs.Responses;
using IoTPlatform.Helpers;
using IoTPlatform.Models;
using IoTPlatform.Services.Guards;
using Microsoft.EntityFrameworkCore;

namespace IoTPlatform.Services;

/// <summary>
/// 档案服务实现（使用仓储模式）
/// </summary>
public class ArchiveService : IArchiveService
{
    private readonly IArchiveRepository _archiveRepository;
    private readonly IDeviceRepository _deviceRepository;
    private readonly IUnitOfWork _unitOfWork;

    private readonly ICustomerScopeGuard? _scopeGuard;

    /// <summary>守卫依赖缺失时 fail-closed，禁止静默降级为无校验写入。</summary>
    private ICustomerScopeGuard RequireGuard() =>
        _scopeGuard ?? throw new ForbiddenAccessException("缺少客户范围守卫，拒绝写入");

    /// <summary>配置 AppCode="system" 属于平台共享行（设计 §3.5）。</summary>
    private static bool IsSystemRow(string? appCode) =>
        string.Equals(appCode, "system", StringComparison.OrdinalIgnoreCase);

    /// <summary>写侧守卫：共享行仅超管可写；租户行按 Controller 下推的 scope 判定。</summary>
    private Task AssertWriteAsync(CustomerScope? scope, string? appCode, CancellationToken ct = default) =>
        RequireGuard().AssertWriteScopeByAppCodeAsync(scope, appCode, IsSystemRow(appCode), false, ct);

    public ArchiveService(
        IArchiveRepository archiveRepository,
        IDeviceRepository deviceRepository,
        IUnitOfWork unitOfWork,
        ICustomerScopeGuard? scopeGuard = null)
    {
        _scopeGuard = scopeGuard;
        _archiveRepository = archiveRepository;
        _deviceRepository = deviceRepository;
        _unitOfWork = unitOfWork;
    }

    /// <summary>
    /// 获取档案列表
    /// </summary>
        /// <summary>旧签名保留：委托到范围下推重载，下级/内部路径零回归。</summary>
    public Task<PagedResponse<ArchiveDto>> GetArchivesAsync(int page, int pageSize, string? keyword, string? type, long? areaId, string? appCode) => GetArchivesAsync(page, pageSize, keyword, type, areaId, appCode, null, null);

    /// <summary>B4/M5：admin 的 appCode=null 不再等于零过滤。</summary>
    public async Task<PagedResponse<ArchiveDto>> GetArchivesAsync(int page, int pageSize, string? keyword, string? type, long? areaId, string? appCode, IReadOnlyCollection<long>? customerIds, IReadOnlyCollection<string>? appCodes = null)
    {
        var baseQuery = _archiveRepository.GetQueryable();
        baseQuery = baseQuery.ApplyQueryScope(appCode, customerIds, appCodes);

        // 租户数据隔离
        if (!string.IsNullOrEmpty(appCode))
        {
            baseQuery = baseQuery.Where(a => a.AppCode == appCode || a.AppCodeTenant == appCode);
        }

        // 类型筛选
        if (!string.IsNullOrEmpty(type))
        {
            baseQuery = baseQuery.Where(a => a.Type == type);
        }

        // 区域筛选
        if (areaId.HasValue)
        {
            baseQuery = baseQuery.Where(a => a.AreaId == areaId.Value);
        }

        // 关键词搜索
        if (!string.IsNullOrEmpty(keyword))
        {
            baseQuery = baseQuery.Where(a =>
                a.Name.Contains(keyword) ||
                (a.Category != null && a.Category.Contains(keyword)));
        }

        var totalCount = await baseQuery.CountAsync();

        var query = baseQuery.Include(a => a.Area);

        var archives = await query
            .OrderByDescending(a => a.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(a => new ArchiveDto
            {
                Id = a.Id,
                Name = a.Name,
                AppCode = a.AppCode,
                Type = a.Type,
                Size = a.Size,
                Date = a.Date,
                Category = a.Category,
                Is3DModel = a.Is3DModel,
                AreaId = a.AreaId,
                AreaName = a.Area != null ? a.Area.Name : null,
                ImageUrl = a.ImageUrl,
                FilePath = a.FilePath,
                SceneConfig = a.SceneConfig,
                CreatedAt = a.CreatedAt,
                UpdatedAt = a.UpdatedAt
            })
            .ToListAsync();

        return PagedResponse<ArchiveDto>.Create(archives, totalCount, page, pageSize);
    }

    /// <summary>
    /// 获取档案详情
    /// </summary>
    public async Task<ArchiveDto?> GetArchiveAsync(long id, string? appCode)
    {
        var baseQuery = _archiveRepository.GetQueryable();

        // 租户数据隔离
        if (!string.IsNullOrEmpty(appCode))
        {
            baseQuery = baseQuery.Where(a => a.AppCode == appCode || a.AppCodeTenant == appCode);
        }

        var query = baseQuery.Include(a => a.Area);
        var archive = await query.FirstOrDefaultAsync(a => a.Id == id);
        if (archive == null) return null;

        return new ArchiveDto
        {
            Id = archive.Id,
            Name = archive.Name,
            AppCode = archive.AppCode,
            Type = archive.Type,
            Size = archive.Size,
            Date = archive.Date,
            Category = archive.Category,
            Is3DModel = archive.Is3DModel,
            AreaId = archive.AreaId,
            AreaName = archive.Area?.Name,
            ImageUrl = archive.ImageUrl,
            FilePath = archive.FilePath,
            SceneConfig = archive.SceneConfig,
            CreatedAt = archive.CreatedAt,
            UpdatedAt = archive.UpdatedAt
        };
    }

    /// <summary>
    /// 创建档案
    /// </summary>
    public async Task<ArchiveDto> CreateArchiveAsync(CreateArchiveRequest request, CustomerScope? scope = null)
    {
        var archive = new Archive
        {
            Name = request.Name,
            AppCode = request.AppCode,
            AppCodeTenant = request.AppCode,
            Type = request.Type,
            Size = request.Size,
            Date = request.Date,
            Category = request.Category,
            Is3DModel = request.Is3DModel,
            AreaId = request.AreaId,
            ImageUrl = request.ImageUrl,
            FilePath = request.FilePath,
            SceneConfig = request.SceneConfig,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        if (scope != null)
        {
            await AssertWriteAsync(scope, archive.AppCode);
        }

        await _archiveRepository.AddAsync(archive);
        await _unitOfWork.SaveChangesAsync();

        // 重新加载以包含关联数据
        archive = await _archiveRepository.GetQueryable()
            .Include(a => a.Area)
            .FirstOrDefaultAsync(a => a.Id == archive.Id);

        return new ArchiveDto
        {
            Id = archive.Id,
            Name = archive.Name,
            AppCode = archive.AppCode,
            Type = archive.Type,
            Size = archive.Size,
            Date = archive.Date,
            Category = archive.Category,
            Is3DModel = archive.Is3DModel,
            AreaId = archive.AreaId,
            AreaName = archive.Area?.Name,
            ImageUrl = archive.ImageUrl,
            FilePath = archive.FilePath,
            SceneConfig = archive.SceneConfig,
            CreatedAt = archive.CreatedAt,
            UpdatedAt = archive.UpdatedAt
        };
    }

    /// <summary>
    /// 更新档案
    /// </summary>
    public async Task<ArchiveDto> UpdateArchiveAsync(long id, UpdateArchiveRequest request, string? appCode, CustomerScope? scope = null)
    {
        var archive = await _archiveRepository.GetQueryable()
            .Include(a => a.Area)
            .FirstOrDefaultAsync(a => a.Id == id);
        if (archive == null)
        {
            throw new InvalidOperationException("档案不存在");
        }

        // 权限检查：档案 AppCode 为空属于归属缺陷，必须 fail-closed（不可当共享行）。
        if (scope != null)
        {
            await AssertWriteAsync(scope, archive.AppCode);
        }
        else if (!string.IsNullOrEmpty(appCode) && archive.AppCode != appCode && archive.AppCodeTenant != appCode)
        {
            throw new UnauthorizedAccessException("无权修改该档案");
        }

        archive.Name = request.Name;
        archive.Type = request.Type ?? archive.Type;
        archive.Size = request.Size;
        archive.Date = request.Date;
        archive.Category = request.Category;
        archive.Is3DModel = request.Is3DModel;
        archive.AreaId = request.AreaId;
        archive.ImageUrl = request.ImageUrl;
        archive.FilePath = request.FilePath;
        archive.SceneConfig = request.SceneConfig;
        archive.UpdatedAt = DateTime.UtcNow;

        await _archiveRepository.UpdateAsync(archive);
        await _unitOfWork.SaveChangesAsync();

        return new ArchiveDto
        {
            Id = archive.Id,
            Name = archive.Name,
            AppCode = archive.AppCode,
            Type = archive.Type,
            Size = archive.Size,
            Date = archive.Date,
            Category = archive.Category,
            Is3DModel = archive.Is3DModel,
            AreaId = archive.AreaId,
            AreaName = archive.Area?.Name,
            ImageUrl = archive.ImageUrl,
            FilePath = archive.FilePath,
            SceneConfig = archive.SceneConfig,
            CreatedAt = archive.CreatedAt,
            UpdatedAt = archive.UpdatedAt
        };
    }

    /// <summary>
    /// 删除档案
    /// </summary>
    public async Task DeleteArchiveAsync(long id, string? appCode, CustomerScope? scope = null)
    {
        var archive = await _archiveRepository.GetByIdAsync(id);
        if (archive == null)
        {
            throw new InvalidOperationException("档案不存在");
        }

        // 权限检查：档案 AppCode 为空属于归属缺陷，必须 fail-closed（不可当共享行）。
        if (scope != null)
        {
            await AssertWriteAsync(scope, archive.AppCode);
        }
        else if (!string.IsNullOrEmpty(appCode) && archive.AppCode != appCode && archive.AppCodeTenant != appCode)
        {
            throw new UnauthorizedAccessException("无权删除该档案");
        }

        await _archiveRepository.DeleteAsync(archive);
        await _unitOfWork.SaveChangesAsync();
    }

    /// <summary>
    /// 获取档案设备标记
    /// </summary>
    public async Task<List<ArchiveDeviceMarkerDto>> GetArchiveMarkersAsync(long archiveId, string? appCode, CustomerScope? scope = null)
    {
        // 权限检查：验证档案所属租户
        var archive = await _archiveRepository.GetByIdAsync(archiveId);
        if (archive == null)
        {
            throw new InvalidOperationException("档案不存在");
        }

        if (scope != null)
        {
            await RequireGuard().AssertReadScopeByAppCodeAsync(scope, archive.AppCode, false, false);
        }
        else if (!string.IsNullOrEmpty(appCode) && archive.AppCode != appCode && archive.AppCodeTenant != appCode)
        {
            throw new UnauthorizedAccessException("无权查看该档案标记");
        }

        var markers = await _archiveRepository.GetMarkersAsync(archiveId);

        return markers.Select(m => new ArchiveDeviceMarkerDto
        {
            Id = m.Id,
            ArchiveId = m.ArchiveId,
            DeviceId = m.DeviceId,
            Name = m.Name,
            DeviceType = m.DeviceType,
            Model = m.Model,
            X = m.X,
            Y = m.Y,
            Z = m.Z,
            Sensors = m.Sensors,
            CreatedAt = m.CreatedAt
        }).ToList();
    }
}
