using IoTPlatform.Data.Repositories.Interfaces;
using IoTPlatform.DTOs.Requests;
using IoTPlatform.DTOs.Responses;
using IoTPlatform.Models;

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

    public DeviceService(
        IDeviceRepository deviceRepository,
        IAreaRepository areaRepository,
        IProjectRepository projectRepository,
        IUnitOfWork unitOfWork)
    {
        _deviceRepository = deviceRepository;
        _areaRepository = areaRepository;
        _projectRepository = projectRepository;
        _unitOfWork = unitOfWork;
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
            deviceDtos.Add(deviceDto);
        }

        return PagedResponse<DeviceDto>.Create(deviceDtos, totalCount, page, pageSize);
    }

    /// <summary>
    /// 获取设备详情
    /// </summary>
    public async Task<DeviceDto?> GetDeviceAsync(long id, string? appCode, List<long>? allowedAreaIds)
    {
        var query = _deviceRepository.Query()
            .Include(d => d.Area)
            .Include(d => d.Project);

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

        var device = await query.FirstOrDefaultAsync(d => d.Id == id);
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

        // 验证项目是否存在
        Project? project = null;
        if (request.ProjectId.HasValue)
        {
            project = await _projectRepository.GetByIdAsync(request.ProjectId.Value);
            if (project == null)
            {
                throw new InvalidOperationException("项目不存在");
            }
        }

        var device = new Device
        {
            AppCode = request.AppCode,
            Name = request.Name,
            Model = request.Model,
            SerialNumber = request.SerialNumber,
            Category = request.Category,
            Location = request.Location,
            AreaId = request.AreaId,
            ProjectId = request.ProjectId,
            ProjectName = request.ProjectName,
            EnergyTypes = request.EnergyTypes,
            Status = request.Status,
            InstallDate = request.InstallDate,
            LastMaintenance = request.LastMaintenance,
            Supplier = request.Supplier,
            WarrantyDate = request.WarrantyDate,
            Power = request.Power,
            Voltage = request.Voltage,
            MeterInstalled = request.MeterInstalled,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _deviceRepository.AddAsync(device);
        await _unitOfWork.SaveChangesAsync();

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
        device.EnergyTypes = request.EnergyTypes;
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
        var hasDataRecords = await _deviceRepository.Query().AnyAsync(r => r.DeviceId == id);
        var hasSensors = await _deviceRepository.Query().AnyAsync(s => s.Sensors != null && s.Sensors.Any());
        var hasAreaDevices = _deviceRepository.Query().Any(d => d.AreaId == id);

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

        var query = _deviceRepository.Query()
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
        var query = _deviceRepository.Query()
            .Include(d => d.Area)
            .Include(d => d.Project)
            .Include(d => d.Sensors);

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

        var device = await query.FirstOrDefaultAsync(d => d.Id == id);
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
            var count = await _deviceRepository.Query()
                .Where(d => d.AreaId == areaId)
                .CountAsync();
            area.DeviceCount = count;
            await _areaRepository.UpdateAsync(area);
            await _unitOfWork.SaveChangesAsync();
        }
    }
}
