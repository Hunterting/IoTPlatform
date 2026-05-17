using IoTPlatform.Data;
using IoTPlatform.Data.Repositories.Interfaces;
using IoTPlatform.DTOs.Requests;
using IoTPlatform.DTOs.Responses;
using IoTPlatform.Helpers;
using IoTPlatform.Models;
using System.Data.Common;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace IoTPlatform.Services;

/// <summary>
/// 设备服务实现（使用仓储模式）
/// </summary>
public class DeviceService : IDeviceService
{
    private readonly IDeviceRepository _deviceRepository;
    private readonly IAreaRepository _areaRepository;
    private readonly IProjectRepository _projectRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly AppDbContext _dbContext;
    private readonly ILogRepository _logRepository;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<DeviceService> _logger;

    public DeviceService(
        IDeviceRepository deviceRepository,
        IAreaRepository areaRepository,
        IProjectRepository projectRepository,
        IUnitOfWork unitOfWork,
        AppDbContext dbContext,
        ILogRepository logRepository,
        IHttpContextAccessor httpContextAccessor,
        ILogger<DeviceService> logger)
    {
        _deviceRepository = deviceRepository;
        _areaRepository = areaRepository;
        _projectRepository = projectRepository;
        _unitOfWork = unitOfWork;
        _dbContext = dbContext;
        _logRepository = logRepository;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    // Ensure EnergyTypes is valid JSON for JSON column storage
    private string? NormalizeEnergyTypesJson(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;

        input = input.Trim();

        // If already valid JSON, return as-is
        try
        {
            if ((input.StartsWith("{") && input.EndsWith("}")) || (input.StartsWith("[") && input.EndsWith("]")))
            {
                // validate
                using var doc = JsonDocument.Parse(input);
                return input;
            }
        }
        catch
        {
            // not valid JSON, will transform below
        }

        // If comma separated, split into array
        if (input.Contains(","))
        {
            var parts = input.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Trim())
                .Where(p => !string.IsNullOrEmpty(p))
                .ToArray();
            return JsonSerializer.Serialize(parts);
        }

        // Single value -> serialize as single-element array
        return JsonSerializer.Serialize(new[] { input });
    }

    // Helper to extract current user info from HttpContext (if available)
    private (long userId, string? userName, string? role, string? appCode, string? ip) GetCurrentUserContext()
    {
        try
        {
            var ctx = _httpContextAccessor?.HttpContext;
            if (ctx == null) return (0, null, null, null, null);

            var user = ctx.User;
            var userIdStr = user.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            long userId = 0;
            if (!string.IsNullOrEmpty(userIdStr) && long.TryParse(userIdStr, out var id)) userId = id;
            var userName = user.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value ?? user.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value;
            var role = user.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
            var appCode = user.FindFirst("AppCode")?.Value;
            var ip = ctx.Connection.RemoteIpAddress?.ToString();
            return (userId, userName, role, appCode, ip);
        }
        catch
        {
            return (0, null, null, null, null);
        }
    }

    /// <summary>
    /// 获取设备列表
    /// </summary>
    public async Task<PagedResponse<DeviceDto>> GetDevicesAsync(int page, int pageSize, string? keyword, string? status, string? appCode, List<long>? allowedAreaIds)
    {
        var query = _deviceRepository.GetQueryable(appCode, allowedAreaIds);

        // 状态过滤
        if (!string.IsNullOrEmpty(status))
        {
            query = query.Where(d => d.Status == status);
        }

        // 关键词搜索
        if (!string.IsNullOrEmpty(keyword))
        {
            query = query.Where(d =>
                d.Name.Contains(keyword) ||
                (d.Model != null && d.Model.Contains(keyword)) ||
                (d.SerialNumber != null && d.SerialNumber.Contains(keyword)) ||
                (d.Location != null && d.Location.Contains(keyword)));
        }

        var totalCount = await query.CountAsync();
        var devices = await query
            .Include(d => d.Project) // 加载项目导航属性
            .OrderByDescending(d => d.UpdatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        // 转换为DTO
        var deviceDtos = new List<DeviceDto>();
        foreach (var device in devices)
        {
            Area? area = null;
            if (device.AreaId.HasValue)
            {
                area = await _areaRepository.GetByIdAsync(device.AreaId.Value);
            }

            var deviceDto = new DeviceDto
            {
                Id = device.Id,
                AppCode = device.AppCode,
                Name = device.Name,
                Model = device.Model,
                SerialNumber = device.SerialNumber,
                Category = device.Category,
                Location = device.Location,
                AreaId = device.AreaId,
                AreaName = area?.Name,
                ProjectId = device.ProjectId,
                ProjectName = device.Project?.Name ?? device.ProjectName,
                EnergyTypes = device.EnergyTypes,
                Status = device.Status,
                InstallDate = device.InstallDate,
                LastMaintenance = device.LastMaintenance,
                Supplier = device.Supplier,
                WarrantyDate = device.WarrantyDate,
                Power = device.Power,
                Voltage = device.Voltage,
                MeterInstalled = device.MeterInstalled,
                CreatedAt = device.CreatedAt,
                UpdatedAt = device.UpdatedAt
            };
            deviceDtos.Add(deviceDto);
        }

        return PagedResponse<DeviceDto>.Create(deviceDtos, totalCount, page, pageSize);
    }

    /// <summary>
    /// 获取设备详情
    /// </summary>
    public async Task<DeviceDto?> GetDeviceAsync(long id, string? appCode, List<long>? allowedAreaIds)
    {
        var query = _deviceRepository.GetQueryable();

        // 租户过滤
        if (!string.IsNullOrEmpty(appCode))
        {
            query = query.Where(d => d.AppCode == appCode);
        }

        // 区域权限过滤
        if (allowedAreaIds != null && allowedAreaIds.Count > 0)
        {
            query = query.Where(d => d.AreaId == null || allowedAreaIds.Contains(d.AreaId.Value));
        }

        var device = await query
            .Include(d => d.Area)
            .Include(d => d.Project)
            .FirstOrDefaultAsync(d => d.Id == id);
        if (device == null) return null;

        return new DeviceDto
        {
            Id = device.Id,
            AppCode = device.AppCode,
            Name = device.Name,
            Model = device.Model,
            SerialNumber = device.SerialNumber,
            Category = device.Category,
            Location = device.Location,
            AreaId = device.AreaId,
            AreaName = device.Area?.Name,
            ProjectId = device.ProjectId,
            ProjectName = device.ProjectName,
            EnergyTypes = device.EnergyTypes,
            Status = device.Status,
            InstallDate = device.InstallDate,
            LastMaintenance = device.LastMaintenance,
            Supplier = device.Supplier,
            WarrantyDate = device.WarrantyDate,
            Power = device.Power,
            Voltage = device.Voltage,
            MeterInstalled = device.MeterInstalled,
            CreatedAt = device.CreatedAt,
            UpdatedAt = device.UpdatedAt
        };
    }

    /// <summary>
    /// 创建设备
    /// </summary>
    public async Task<DeviceDto> CreateDeviceAsync(CreateDeviceRequest request)
    {
        // 验证区域是否存在，如果不存在则设置为null
        // 使用 DbContext 直接查询，避免仓储层全局过滤器干扰
        Area? area = null;
        long? validAreaId = null;
        if (request.AreaId.HasValue)
        {
            try
            {
                area = await _dbContext.Areas.AsNoTracking().FirstOrDefaultAsync(a => a.Id == request.AreaId.Value);
                if (area != null)
                {
                    validAreaId = request.AreaId.Value;

                    // 如果提供了 AppCode，确保区域属于相同的 AppCode
                    if (!string.IsNullOrEmpty(request.AppCode) && area.AppCode != request.AppCode)
                    {
                        _logger.LogWarning("区域ID {AreaId} 的 AppCode 与请求不匹配（{AreaAppCode} != {RequestAppCode}），将不关联区域", request.AreaId.Value, area.AppCode, request.AppCode);
                        validAreaId = null;
                        area = null;
                        try
                        {
                            var ctx = GetCurrentUserContext();
                            await _logRepository.LogOperationAsync(
                                userId: ctx.userId,
                                userName: ctx.userName,
                                role: ctx.role,
                                module: "DeviceService:Validate",
                                action: "ValidateAreaAppCodeMismatch",
                                target: $"Area:{request.AreaId.Value}",
                                detail: $"区域ID {request.AreaId.Value} 的 AppCode ({area?.AppCode}) 与请求 AppCode ({request.AppCode}) 不匹配，设备将不关联区域",
                                ip: ctx.ip,
                                status: "failed",
                                duration: null,
                                appCode: ctx.appCode ?? request.AppCode
                            );
                        }
                        catch { }
                    }
                }
                else
                {
                    _logger.LogWarning("区域ID {AreaId} 不存在，设备将不关联区域", request.AreaId.Value);
                    try
                    {
                        var ctx = GetCurrentUserContext();
                        await _logRepository.LogOperationAsync(
                            userId: ctx.userId,
                            userName: ctx.userName,
                            role: ctx.role,
                            module: "DeviceService:Validate",
                            action: "ValidateAreaNotFound",
                            target: $"Area:{request.AreaId.Value}",
                            detail: $"区域ID {request.AreaId.Value} 不存在，设备将不关联区域",
                            ip: ctx.ip,
                            status: "success",
                            duration: null,
                            appCode: ctx.appCode ?? request.AppCode
                        );
                    }
                    catch
                    {
                        // 写数据库日志失败不影响主流程
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "查询区域ID {AreaId} 时发生错误", request.AreaId.Value);
                try
                {
                    var ctx = GetCurrentUserContext();
                    await _logRepository.LogOperationAsync(
                        userId: ctx.userId,
                        userName: ctx.userName,
                        role: ctx.role,
                        module: "DeviceService:Validate",
                        action: "ValidateAreaError",
                        target: $"Area:{request.AreaId.Value}",
                        detail: $"查询区域ID {request.AreaId.Value} 时发生错误: {ex}",
                        ip: ctx.ip,
                        status: "failed",
                        duration: null,
                        appCode: ctx.appCode ?? request.AppCode
                    );
                }
                catch
                {
                    // 写数据库日志失败不影响主流程
                }
            }
        }

        // 验证项目是否存在，如果不存在则设置为null
        Project? project = null;
        long? validProjectId = null;string? validProjectName = null;
        if (request.ProjectId.HasValue)
        {
            try
            {
                project = await _dbContext.Projects.AsNoTracking().FirstOrDefaultAsync(p => p.Id == request.ProjectId.Value);
                if (project != null)
                {
                    validProjectId = request.ProjectId.Value;
                    validProjectName = project.Name;

                    // 如果提供了 AppCode，确保项目属于相同的 AppCode
                    if (!string.IsNullOrEmpty(request.AppCode) && project.AppCode != request.AppCode)
                    {
                        _logger.LogWarning("项目ID {ProjectId} 的 AppCode 与请求不匹配（{ProjectAppCode} != {RequestAppCode}），将不关联项目", request.ProjectId.Value, project.AppCode, request.AppCode);
                        // 不关联项目
                        validProjectId = null;
                    }
                }
                else
                {
                    _logger.LogWarning("项目ID {ProjectId} 不存在，设备将不关联项目", request.ProjectId.Value);
                    try
                    {
                        var ctx = GetCurrentUserContext();
                        await _logRepository.LogOperationAsync(
                            userId: ctx.userId,
                            userName: ctx.userName,
                            role: ctx.role,
                            module: "DeviceService:Validate",
                            action: "ValidateProjectNotFound",
                            target: $"Project:{request.ProjectId.Value}",
                            detail: $"项目ID {request.ProjectId.Value} 不存在，设备将不关联项目",
                            ip: ctx.ip,
                            status: "success",
                            duration: null,
                            appCode: ctx.appCode ?? request.AppCode
                        );
                    }
                    catch
                    {
                        // 写数据库日志失败不影响主流程
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "查询项目ID {ProjectId} 时发生错误", request.ProjectId.Value);
                try
                {
                    var ctx = GetCurrentUserContext();
                    await _logRepository.LogOperationAsync(
                        userId: ctx.userId,
                        userName: ctx.userName,
                        role: ctx.role,
                        module: "DeviceService:Validate",
                        action: "ValidateProjectError",
                        target: $"Project:{request.ProjectId.Value}",
                        detail: $"查询项目ID {request.ProjectId.Value} 时发生错误: {ex}",
                        ip: ctx.ip,
                        status: "failed",
                        duration: null,
                        appCode: ctx.appCode ?? request.AppCode
                    );
                }
                catch
                {
                    // 写数据库日志失败不影响主流程
                }
            }
        }

        // 使用一个全新的实体实例来插入，避免任何已跟踪的导航属性或影子属性导致的异常（例如 ProjectId1）
        Device device;
        var newDevice = new Device
        {
            // Ensure Id is zero so the database assigns the identity value
            Id = 0,
            AppCode = request.AppCode,
            Name = request.Name,
            Model = request.Model,
            SerialNumber = request.SerialNumber,
            Category = request.Category,
            Location = request.Location,
            AreaId = validAreaId,
            ProjectId = validProjectId,
            ProjectName = validProjectName,
            EnergyTypes = NormalizeEnergyTypesJson(request.EnergyTypes),
            Status = request.Status,
            InstallDate = request.InstallDate,
            LastMaintenance = request.LastMaintenance,
            Supplier = request.Supplier,
            WarrantyDate = request.WarrantyDate,
            Power = request.Power,
            Voltage = request.Voltage,
            MeterInstalled = request.MeterInstalled,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            // 确保导航为 null
            Project = null,
            Area = null
        };

        // 清除 ChangeTracker，确保没有遗留的跟踪实体或影子属性（例如 ProjectId1）
        try
        {
            _dbContext.ChangeTracker.Clear();
        }
        catch { }

        try
        {
            // 使用 EF Core 正常插入（若此前有跟踪冲突，之前已尝试分离相关实体并将导航置 null）
            _dbContext.Devices.Add(newDevice);
            await _dbContext.SaveChangesAsync();
            device = newDevice;
        }
        catch (Exception ex)
        {
            // 如果是 EF Core 的 DbUpdateException，记录更详细的变更追踪信息，便于定位外键约束问题
            if (ex is DbUpdateException dbEx)
            {
                try
                {
                    var entries = _dbContext.ChangeTracker.Entries()
                        .Select(e => new
                        {
                            Entity = e.Entity?.GetType().FullName,
                            State = e.State.ToString(),
                            Values = e.CurrentValues.Properties.ToDictionary(p => p.Name, p => e.CurrentValues[p.Name])
                        })
                        .ToList();

                    _logger.LogError(dbEx, "DbUpdateException 保存设备时发生错误，ChangeTracker 条目：{@Entries}", entries);
                }
                catch (Exception tEx)
                {
                    _logger.LogWarning(tEx, "记录 ChangeTracker 信息时失败");
                }
            }

            _logger.LogError(ex, "保存设备时发生错误. Device: {@Device}. Inner: {Inner}", newDevice, ex.InnerException?.Message);
            try
            {
                var ctx = GetCurrentUserContext();
                var detail = ex is DbUpdateException ? $"DbUpdateException: {ex.InnerException?.Message ?? ex.Message}" : ex.ToString();
                await _logRepository.LogOperationAsync(
                    userId: ctx.userId,
                    userName: ctx.userName,
                    role: ctx.role,
                    module: "DeviceService:Create",
                    action: "CreateDeviceError",
                    target: $"DeviceTemp",
                    detail: $"保存设备时发生错误. Device: {@newDevice}. Exception: {detail}",
                    ip: ctx.ip,
                    status: "failed",
                    duration: null,
                    appCode: ctx.appCode ?? request.AppCode
                );
            }
            catch
            {
                // 写数据库日志失败不影响主流程
            }

            throw;
        }

        // 更新区域设备计数
        if (area != null)
        {
            await UpdateAreaDeviceCountAsync(area.Id);
        }

        return new DeviceDto
        {
            Id = device.Id,
            AppCode = device.AppCode,
            Name = device.Name,
            Model = device.Model,
            SerialNumber = device.SerialNumber,
            Category = device.Category,
            Location = device.Location,
            AreaId = device.AreaId,
            AreaName = area?.Name,
            ProjectId = device.ProjectId,
            ProjectName = device.ProjectName,
            EnergyTypes = device.EnergyTypes,
            Status = device.Status,
            InstallDate = device.InstallDate,
            LastMaintenance = device.LastMaintenance,
            Supplier = device.Supplier,
            WarrantyDate = device.WarrantyDate,
            Power = device.Power,
            Voltage = device.Voltage,
            MeterInstalled = device.MeterInstalled,
            CreatedAt = device.CreatedAt,
            UpdatedAt = device.UpdatedAt
        };
    }

    /// <summary>
    /// 更新设备
    /// </summary>
    public async Task<DeviceDto> UpdateDeviceAsync(long id, UpdateDeviceRequest request)
    {
        var device = await _deviceRepository.GetByIdAsync(id);
        if (device == null)
        {
            throw new InvalidOperationException("设备不存在");
        }

        var oldAreaId = device.AreaId;

        // 验证区域是否存在
        Area? area = null;
        if (request.AreaId.HasValue)
        {
            area = await _areaRepository.GetByIdAsync(request.AreaId.Value);
            if (area == null)
            {
                throw new InvalidOperationException("区域不存在");
            }
        }

        device.Name = request.Name;
        device.Model = request.Model;
        device.SerialNumber = request.SerialNumber;
        device.Category = request.Category;
        device.Location = request.Location;
        device.AreaId = request.AreaId;
        device.ProjectId = request.ProjectId;
        device.ProjectName = request.ProjectName;
        device.EnergyTypes = NormalizeEnergyTypesJson(request.EnergyTypes);
        device.Status = request.Status;
        device.InstallDate = request.InstallDate;
        device.LastMaintenance = request.LastMaintenance;
        device.Supplier = request.Supplier;
        device.WarrantyDate = request.WarrantyDate;
        device.Power = request.Power;
        device.Voltage = request.Voltage;
        device.MeterInstalled = request.MeterInstalled;
        device.UpdatedAt = DateTime.UtcNow;

        await _deviceRepository.UpdateAsync(device);
        await _unitOfWork.SaveChangesAsync();

        // 更新区域设备计数
        if (oldAreaId != device.AreaId)
        {
            if (oldAreaId.HasValue)
            {
                await UpdateAreaDeviceCountAsync(oldAreaId.Value);
            }
            if (device.AreaId.HasValue)
            {
                await UpdateAreaDeviceCountAsync(device.AreaId.Value);
            }
        }

        return new DeviceDto
        {
            Id = device.Id,
            AppCode = device.AppCode,
            Name = device.Name,
            Model = device.Model,
            SerialNumber = device.SerialNumber,
            Category = device.Category,
            Location = device.Location,
            AreaId = device.AreaId,
            AreaName = area?.Name,
            ProjectId = device.ProjectId,
            ProjectName = device.ProjectName,
            EnergyTypes = device.EnergyTypes,
            Status = device.Status,
            InstallDate = device.InstallDate,
            LastMaintenance = device.LastMaintenance,
            Supplier = device.Supplier,
            WarrantyDate = device.WarrantyDate,
            Power = device.Power,
            Voltage = device.Voltage,
            MeterInstalled = device.MeterInstalled,
            CreatedAt = device.CreatedAt,
            UpdatedAt = device.UpdatedAt
        };
    }

    /// <summary>
    /// 删除设备
    /// </summary>
    public async Task DeleteDeviceAsync(long id)
    {
        var device = await _deviceRepository.GetByIdAsync(id);
        if (device == null)
        {
            throw new InvalidOperationException("设备不存在");
        }

        var areaId = device.AreaId;

        // 检查是否有关联数据
        var hasDataRecords = await _dbContext.DeviceDataRecords.AnyAsync(r => r.DeviceId == id);
        var hasSensors = await _deviceRepository.GetQueryable().AnyAsync(s => s.Sensors != null && s.Sensors.Any());
        var hasAreaDevices = _deviceRepository.GetQueryable().Any(d => d.AreaId == id);

        if (hasDataRecords || hasSensors || hasAreaDevices)
        {
            throw new InvalidOperationException("设备有关联数据，无法删除");
        }

        await _deviceRepository.DeleteAsync(device);
        await _unitOfWork.SaveChangesAsync();

        // 更新区域设备计数
        if (areaId.HasValue)
        {
            await UpdateAreaDeviceCountAsync(areaId.Value);
        }
    }

    /// <summary>
    /// 根据区域获取设备列表
    /// </summary>
    public async Task<List<DeviceDto>> GetDevicesByAreaAsync(long areaId, string? appCode, List<long>? allowedAreaIds)
    {
        // 区域权限过滤
        if (allowedAreaIds != null && allowedAreaIds.Count > 0 && !allowedAreaIds.Contains(areaId))
        {
            return new List<DeviceDto>();
        }

        var query = _deviceRepository.GetQueryable()
            .Include(d => d.Area)
            .Where(d => d.AreaId == areaId);

        // 租户过滤
        if (!string.IsNullOrEmpty(appCode))
        {
            query = query.Where(d => d.AppCode == appCode);
        }

        return await query
            .OrderBy(d => d.Name)
            .Select(d => new DeviceDto
            {
                Id = d.Id,
                AppCode = d.AppCode,
                Name = d.Name,
                Model = d.Model,
                SerialNumber = d.SerialNumber,
                Category = d.Category,
                Location = d.Location,
                AreaId = d.AreaId,
                AreaName = d.Area != null ? d.Area.Name : null,
                ProjectId = d.ProjectId,
                ProjectName = d.ProjectName,
                EnergyTypes = d.EnergyTypes,
                Status = d.Status,
                InstallDate = d.InstallDate,
                LastMaintenance = d.LastMaintenance,
                Supplier = d.Supplier,
                WarrantyDate = d.WarrantyDate,
                Power = d.Power,
                Voltage = d.Voltage,
                MeterInstalled = d.MeterInstalled,
                CreatedAt = d.CreatedAt,
                UpdatedAt = d.UpdatedAt
            })
            .ToListAsync();
    }

    /// <summary>
    /// 获取设备详情（包含传感器）
    /// </summary>
    public async Task<DeviceDetailDto?> GetDeviceDetailAsync(long id, string? appCode, List<long>? allowedAreaIds)
    {
        var query = _deviceRepository.GetQueryable();

        // 租户过滤
        if (!string.IsNullOrEmpty(appCode))
        {
            query = query.Where(d => d.AppCode == appCode);
        }

        // 区域权限过滤
        if (allowedAreaIds != null && allowedAreaIds.Count > 0)
        {
            query = query.Where(d => d.AreaId == null || allowedAreaIds.Contains(d.AreaId.Value));
        }

        var device = await query
            .Include(d => d.Area)
            .Include(d => d.Project)
            .Include(d => d.Sensors)
            .FirstOrDefaultAsync(d => d.Id == id);
        if (device == null) return null;

        return new DeviceDetailDto
        {
            Id = device.Id,
            AppCode = device.AppCode,
            Name = device.Name,
            Model = device.Model,
            SerialNumber = device.SerialNumber,
            Category = device.Category,
            Location = device.Location,
            AreaId = device.AreaId,
            AreaName = device.Area?.Name,
            ProjectId = device.ProjectId,
            ProjectName = device.ProjectName,
            EnergyTypes = device.EnergyTypes,
            Status = device.Status,
            InstallDate = device.InstallDate,
            LastMaintenance = device.LastMaintenance,
            Supplier = device.Supplier,
            WarrantyDate = device.WarrantyDate,
            Power = device.Power,
            Voltage = device.Voltage,
            MeterInstalled = device.MeterInstalled,
            CreatedAt = device.CreatedAt,
            UpdatedAt = device.UpdatedAt,
            Sensors = device.Sensors?.Select(s => new DeviceSensorDto
            {
                Id = s.Id,
                DeviceId = s.DeviceId,
                Name = s.Name,
                SensorType = s.SensorType,
                LastValue = s.LastValue,
                Unit = s.Unit
            }).ToList()
        };
    }

    /// <summary>
    /// 更新区域设备计数
    /// </summary>
    private async Task UpdateAreaDeviceCountAsync(long areaId)
    {
        var area = await _areaRepository.GetByIdAsync(areaId);
        if (area != null)
        {
            var count = await _deviceRepository.GetQueryable()
                .Where(d => d.AreaId == areaId)
                .CountAsync();
            area.DeviceCount = count;
            await _areaRepository.UpdateAsync(area);
            await _unitOfWork.SaveChangesAsync();
        }
    }
}
