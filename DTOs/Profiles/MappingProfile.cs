using AutoMapper;
using IoTPlatform.DTOs.Requests;
using IoTPlatform.DTOs.Responses;
using IoTPlatform.Models;
using System.Text.Json;

namespace IoTPlatform.DTOs.Profiles;

/// <summary>
/// AutoMapper映射配置
/// </summary>
public class MappingProfile : Profile
{
    public MappingProfile()
    {
        ConfigureUserMappings();
        ConfigureRoleMappings();
        ConfigureCustomerMappings();
        ConfigureProjectMappings();
        ConfigureAreaMappings();
        ConfigureDeviceMappings();
        ConfigureAlertMappings();
        ConfigureWorkOrderMappings();
        ConfigureArchiveMappings();
        ConfigureProtocolConfigMappings();
        ConfigureDataRuleMappings();
        ConfigureDictionaryMappings();
        ConfigureSystemSettingMappings();
        ConfigureETLTaskMappings();
        ConfigureMonitoringMappings();
        ConfigureLogMappings();
    }

    private void ConfigureUserMappings()
    {
        CreateMap<CreateUserRequest, User>()
            .ForMember(dest => dest.CreatedAt, opt => opt.Ignore())
            .ForMember(dest => dest.UpdatedAt, opt => opt.Ignore());

        CreateMap<UpdateUserRequest, User>()
            .ForMember(dest => dest.CreatedAt, opt => opt.Ignore())
            .ForMember(dest => dest.UpdatedAt, opt => opt.Ignore());

        CreateMap<User, UserDto>()
            .ForMember(dest => dest.CustomerName, opt => opt.MapFrom(src => src.Customer != null ? src.Customer.Name : null))
            .ForMember(dest => dest.AllowedAreaIds, opt => opt.MapFrom(src => 
                !string.IsNullOrEmpty(src.AllowedAreaIds) 
                    ? src.AllowedAreaIds.Split(',').Select(long.Parse).ToList()
                    : null));

        CreateMap<User, UserDetailDto>()
            .IncludeBase<User, UserDto>()
            .ForMember(dest => dest.LastLoginAt, opt => opt.Ignore()); // 需要从登录日志获取

        // LoginResponse中的UserDto映射 - 使用UserDto而不是LoginResponse.UserDto
        CreateMap<User, UserDto>()
            .ForMember(dest => dest.CustomerName, opt => opt.MapFrom(src => src.Customer != null ? src.Customer.Name : null))
            .ForMember(dest => dest.AllowedAreaIds, opt => opt.MapFrom(src =>
                !string.IsNullOrEmpty(src.AllowedAreaIds)
                    ? src.AllowedAreaIds.Split(',').Select(long.Parse).ToList()
                    : null));
    }

    private void ConfigureRoleMappings()
    {
        CreateMap<CreateRoleRequest, Role>()
            .ForMember(dest => dest.Permissions, opt => opt.MapFrom(src =>
                src.Permissions != null ? JsonSerializer.Serialize(src.Permissions, (JsonSerializerOptions)null) : null))
            .ForMember(dest => dest.AppCode, opt => opt.Ignore())
            .ForMember(dest => dest.IsSystem, opt => opt.MapFrom(src => false))
            .ForMember(dest => dest.CreatedAt, opt => opt.Ignore())
            .ForMember(dest => dest.UpdatedAt, opt => opt.Ignore());

        CreateMap<UpdateRoleRequest, Role>()
            .ForMember(dest => dest.Code, opt => opt.Ignore())
            .ForMember(dest => dest.Permissions, opt => opt.MapFrom(src =>
                src.Permissions != null ? JsonSerializer.Serialize(src.Permissions, (JsonSerializerOptions)null) : null))
            .ForMember(dest => dest.AppCode, opt => opt.Ignore())
            .ForMember(dest => dest.IsSystem, opt => opt.Ignore())
            .ForMember(dest => dest.CreatedAt, opt => opt.Ignore())
            .ForMember(dest => dest.UpdatedAt, opt => opt.Ignore());

        CreateMap<Role, RoleDto>()
            .ForMember(dest => dest.PermissionList, opt => opt.MapFrom(src =>
                !string.IsNullOrEmpty(src.Permissions)
                    ? JsonSerializer.Deserialize<List<string>>(src.Permissions, (JsonSerializerOptions)null)
                    : null));
    }

    private void ConfigureCustomerMappings()
    {
        CreateMap<Customer, CustomerDto>()
            .ForMember(dest => dest.DeviceCount, opt => opt.MapFrom(src => 
                src.Projects != null ? src.Projects.Sum(p => p.DeviceCount) : 0));
    }

    private void ConfigureProjectMappings()
    {
        // 需要先创建Project相关的DTO
        // CreateMap<CreateProjectRequest, Project>();
        // CreateMap<Project, ProjectDto>();
    }

    private void ConfigureAreaMappings()
    {
        CreateMap<CreateAreaRequest, Area>()
            .ForMember(dest => dest.DeviceCount, opt => opt.Ignore())
            .ForMember(dest => dest.CreatedAt, opt => opt.Ignore())
            .ForMember(dest => dest.UpdatedAt, opt => opt.Ignore());

        CreateMap<UpdateAreaRequest, Area>()
            .ForMember(dest => dest.Type, opt => opt.Ignore())
            .ForMember(dest => dest.ParentId, opt => opt.Ignore())
            .ForMember(dest => dest.CustomerId, opt => opt.Ignore())
            .ForMember(dest => dest.AppCode, opt => opt.Ignore())
            .ForMember(dest => dest.DeviceCount, opt => opt.Ignore())
            .ForMember(dest => dest.CreatedAt, opt => opt.Ignore())
            .ForMember(dest => dest.UpdatedAt, opt => opt.Ignore());

        CreateMap<Area, AreaDto>()
            .ForMember(dest => dest.ParentName, opt => opt.MapFrom(src => src.Parent != null ? src.Parent.Name : null))
            .ForMember(dest => dest.CustomerName, opt => opt.MapFrom(src => src.Customer != null ? src.Customer.Name : null));

        CreateMap<Area, AreaTreeNodeDto>()
            .ForMember(dest => dest.Children, opt => opt.MapFrom(src => src.Children));
    }

    private void ConfigureDeviceMappings()
    {
        CreateMap<CreateDeviceRequest, Device>()
            .ForMember(dest => dest.CreatedAt, opt => opt.Ignore())
            .ForMember(dest => dest.UpdatedAt, opt => opt.Ignore());

        CreateMap<UpdateDeviceRequest, Device>()
            .ForMember(dest => dest.AppCode, opt => opt.Ignore())
            .ForMember(dest => dest.CreatedAt, opt => opt.Ignore())
            .ForMember(dest => dest.UpdatedAt, opt => opt.Ignore());

        CreateMap<Device, DeviceDto>()
            .ForMember(dest => dest.AreaName, opt => opt.MapFrom(src => src.Area != null ? src.Area.Name : null));

        CreateMap<Device, DeviceDetailDto>()
            .IncludeBase<Device, DeviceDto>()
            .ForMember(dest => dest.Sensors, opt => opt.MapFrom(src => src.Sensors));

        CreateMap<DeviceSensor, DeviceSensorDto>();
    }

    private void ConfigureAlertMappings()
    {
        CreateMap<CreateAlertRequest, AlertRecord>()
            .ForMember(dest => dest.AlertNo, opt => opt.Ignore())
            .ForMember(dest => dest.Status, opt => opt.MapFrom(src => "pending"))
            .ForMember(dest => dest.AlertTime, opt => opt.Ignore())
            .ForMember(dest => dest.CreatedAt, opt => opt.Ignore())
            .ForMember(dest => dest.UpdatedAt, opt => opt.Ignore());

        CreateMap<AlertRecord, AlertDto>();

        CreateMap<AlertProcessLog, AlertProcessLogDto>();
    }

    private void ConfigureWorkOrderMappings()
    {
        CreateMap<CreateWorkOrderRequest, WorkOrder>()
            .ForMember(dest => dest.OrderNo, opt => opt.Ignore())
            .ForMember(dest => dest.Status, opt => opt.MapFrom(src => "pending"))
            .ForMember(dest => dest.ReportTime, opt => opt.Ignore())
            .ForMember(dest => dest.DeviceName, opt => opt.Ignore())
            .ForMember(dest => dest.DeviceCode, opt => opt.Ignore())
            .ForMember(dest => dest.AreaName, opt => opt.Ignore())
            .ForMember(dest => dest.CreatedAt, opt => opt.Ignore())
            .ForMember(dest => dest.UpdatedAt, opt => opt.Ignore());

        CreateMap<UpdateWorkOrderRequest, WorkOrder>()
            .ForMember(dest => dest.Type, opt => opt.Ignore())
            .ForMember(dest => dest.CustomerId, opt => opt.Ignore())
            .ForMember(dest => dest.DeviceId, opt => opt.Ignore())
            .ForMember(dest => dest.AreaId, opt => opt.Ignore())
            .ForMember(dest => dest.DeviceName, opt => opt.Ignore())
            .ForMember(dest => dest.DeviceCode, opt => opt.Ignore())
            .ForMember(dest => dest.AreaName, opt => opt.Ignore())
            .ForMember(dest => dest.Reporter, opt => opt.Ignore())
            .ForMember(dest => dest.ReportTime, opt => opt.Ignore())
            .ForMember(dest => dest.Assignee, opt => opt.Ignore())
            .ForMember(dest => dest.AssignTime, opt => opt.Ignore())
            .ForMember(dest => dest.ResolvedTime, opt => opt.Ignore())
            .ForMember(dest => dest.ClosedTime, opt => opt.Ignore())
            .ForMember(dest => dest.ResolveDescription, opt => opt.Ignore())
            .ForMember(dest => dest.ProjectName, opt => opt.Ignore())
            .ForMember(dest => dest.AppCode, opt => opt.Ignore())
            .ForMember(dest => dest.CreatedAt, opt => opt.Ignore())
            .ForMember(dest => dest.UpdatedAt, opt => opt.Ignore());

        CreateMap<WorkOrder, WorkOrderDto>()
            .ForMember(dest => dest.CustomerName, opt => opt.MapFrom(src => src.Customer != null ? src.Customer.Name : null));

        CreateMap<WorkOrderLog, WorkOrderLogDto>();
        CreateMap<WorkOrderAttachment, WorkOrderAttachmentDto>();
    }

    private void ConfigureArchiveMappings()
    {
        CreateMap<CreateArchiveRequest, Archive>()
            .ForMember(dest => dest.AppCodeTenant, opt => opt.Ignore())
            .ForMember(dest => dest.CreatedAt, opt => opt.Ignore())
            .ForMember(dest => dest.UpdatedAt, opt => opt.Ignore());

        CreateMap<UpdateArchiveRequest, Archive>()
            .ForMember(dest => dest.AppCode, opt => opt.Ignore())
            .ForMember(dest => dest.AppCodeTenant, opt => opt.Ignore())
            .ForMember(dest => dest.CreatedAt, opt => opt.Ignore())
            .ForMember(dest => dest.UpdatedAt, opt => opt.Ignore());

        CreateMap<Archive, ArchiveDto>()
            .ForMember(dest => dest.AreaName, opt => opt.MapFrom(src => src.Area != null ? src.Area.Name : null));

        CreateMap<ArchiveDeviceMarker, ArchiveDeviceMarkerDto>()
            .ForMember(dest => dest.DeviceName, opt => opt.MapFrom(src => src.Device != null ? src.Device.Name : null));
    }

    private void ConfigureProtocolConfigMappings()
    {
        CreateMap<CreateProtocolConfigRequest, ProtocolConfig>()
            .ForMember(dest => dest.DeviceIds, opt => opt.MapFrom(src =>
                src.DeviceIds != null ? JsonSerializer.Serialize(src.DeviceIds, (JsonSerializerOptions?)null) : null))
            .ForMember(dest => dest.Config, opt => opt.MapFrom(src =>
                src.Config != null ? JsonSerializer.Serialize(src.Config, (JsonSerializerOptions?)null) : null))
            .ForMember(dest => dest.Status, opt => opt.MapFrom(src => "active"))
            .ForMember(dest => dest.AppCode, opt => opt.Ignore())
            .ForMember(dest => dest.CreatedAt, opt => opt.Ignore())
            .ForMember(dest => dest.UpdatedAt, opt => opt.Ignore());

        CreateMap<UpdateProtocolConfigRequest, ProtocolConfig>()
            .ForMember(dest => dest.Type, opt => opt.Ignore())
            .ForMember(dest => dest.DeviceIds, opt => opt.MapFrom(src =>
                src.DeviceIds != null ? JsonSerializer.Serialize(src.DeviceIds, (JsonSerializerOptions?)null) : null))
            .ForMember(dest => dest.Config, opt => opt.MapFrom(src =>
                src.Config != null ? JsonSerializer.Serialize(src.Config, (JsonSerializerOptions?)null) : null))
            .ForMember(dest => dest.AppCode, opt => opt.Ignore())
            .ForMember(dest => dest.CreatedAt, opt => opt.Ignore())
            .ForMember(dest => dest.UpdatedAt, opt => opt.Ignore());

        CreateMap<ProtocolConfig, ProtocolConfigDto>()
            .ForMember(dest => dest.DeviceIds, opt => opt.MapFrom(src =>
                !string.IsNullOrEmpty(src.DeviceIds)
                    ? JsonSerializer.Deserialize<List<long>>(src.DeviceIds, (JsonSerializerOptions?)null)
                    : null))
            .ForMember(dest => dest.Config, opt => opt.MapFrom(src =>
                !string.IsNullOrEmpty(src.Config)
                    ? JsonSerializer.Deserialize<Dictionary<string, object>>(src.Config, (JsonSerializerOptions?)null)
                    : null));
    }

    private void ConfigureDataRuleMappings()
    {
        CreateMap<CreateDataRuleRequest, DataRule>()
            .ForMember(dest => dest.CreatedAt, opt => opt.Ignore())
            .ForMember(dest => dest.UpdatedAt, opt => opt.Ignore());

        CreateMap<UpdateDataRuleRequest, DataRule>()
            .ForMember(dest => dest.DeviceId, opt => opt.Ignore())
            .ForMember(dest => dest.AreaId, opt => opt.Ignore())
            .ForMember(dest => dest.AppCode, opt => opt.Ignore())
            .ForMember(dest => dest.CreatedAt, opt => opt.Ignore())
            .ForMember(dest => dest.UpdatedAt, opt => opt.Ignore());

        CreateMap<DataRule, DataRuleDto>()
            .ForMember(dest => dest.DeviceName, opt => opt.MapFrom(src => src.Device != null ? src.Device.Name : null))
            .ForMember(dest => dest.AreaName, opt => opt.MapFrom(src => src.Area != null ? src.Area.Name : null));
    }

    private void ConfigureDictionaryMappings()
    {
        CreateMap<CreateDictionaryTypeRequest, DictionaryTypeConfig>()
            .ForMember(dest => dest.CreatedAt, opt => opt.Ignore())
            .ForMember(dest => dest.UpdatedAt, opt => opt.Ignore());

        CreateMap<UpdateDictionaryTypeRequest, DictionaryTypeConfig>()
            .ForMember(dest => dest.Code, opt => opt.Ignore())
            .ForMember(dest => dest.AppCode, opt => opt.Ignore())
            .ForMember(dest => dest.CreatedAt, opt => opt.Ignore())
            .ForMember(dest => dest.UpdatedAt, opt => opt.Ignore());

        CreateMap<DictionaryTypeConfig, DictionaryTypeDto>();

        CreateMap<CreateDictionaryItemRequest, DictionaryItem>()
            .ForMember(dest => dest.Status, opt => opt.MapFrom(src => "active"))
            .ForMember(dest => dest.CreatedAt, opt => opt.Ignore())
            .ForMember(dest => dest.UpdatedAt, opt => opt.Ignore());

        CreateMap<UpdateDictionaryItemRequest, DictionaryItem>()
            .ForMember(dest => dest.Type, opt => opt.Ignore())
            .ForMember(dest => dest.Code, opt => opt.Ignore())
            .ForMember(dest => dest.AppCode, opt => opt.Ignore())
            .ForMember(dest => dest.CreatedAt, opt => opt.Ignore())
            .ForMember(dest => dest.UpdatedAt, opt => opt.Ignore());

        CreateMap<DictionaryItem, DictionaryItemDto>();
    }

    private void ConfigureSystemSettingMappings()
    {
        CreateMap<CreateSettingRequest, SystemSetting>()
            .ForMember(dest => dest.CreatedAt, opt => opt.Ignore())
            .ForMember(dest => dest.UpdatedAt, opt => opt.Ignore());

        CreateMap<UpdateSettingRequest, SystemSetting>()
            .ForMember(dest => dest.Key, opt => opt.Ignore())
            .ForMember(dest => dest.Category, opt => opt.Ignore())
            .ForMember(dest => dest.AppCode, opt => opt.Ignore())
            .ForMember(dest => dest.CreatedAt, opt => opt.Ignore())
            .ForMember(dest => dest.UpdatedAt, opt => opt.Ignore());

        CreateMap<SystemSetting, SettingDto>();
    }

    private void ConfigureETLTaskMappings()
    {
        CreateMap<CreateETLTaskRequest, ETLTask>()
            .ForMember(dest => dest.Status, opt => opt.MapFrom(src => "paused"))
            .ForMember(dest => dest.LastRunTime, opt => opt.Ignore())
            .ForMember(dest => dest.NextRunTime, opt => opt.Ignore())
            .ForMember(dest => dest.CreatedAt, opt => opt.Ignore())
            .ForMember(dest => dest.UpdatedAt, opt => opt.Ignore());

        CreateMap<UpdateETLTaskRequest, ETLTask>()
            .ForMember(dest => dest.TaskType, opt => opt.Ignore())
            .ForMember(dest => dest.AppCode, opt => opt.Ignore())
            .ForMember(dest => dest.LastRunTime, opt => opt.Ignore())
            .ForMember(dest => dest.NextRunTime, opt => opt.Ignore())
            .ForMember(dest => dest.CreatedAt, opt => opt.Ignore())
            .ForMember(dest => dest.UpdatedAt, opt => opt.Ignore());

        CreateMap<ETLTask, ETLTaskDto>();
    }

    private void ConfigureMonitoringMappings()
    {
        CreateMap<DeviceDataRecord, MonitoringDataDto>()
            .ForMember(dest => dest.DeviceName, opt => opt.MapFrom(src => src.Device != null ? src.Device.Name : null))
            .ForMember(dest => dest.AreaId, opt => opt.MapFrom(src => src.Device != null ? src.Device.AreaId : null))
            .ForMember(dest => dest.AreaName, opt => opt.MapFrom(src =>
                src.Device != null && src.Device.Area != null ? src.Device.Area.Name : null))
            .ForMember(dest => dest.SensorValues, opt => opt.MapFrom(src =>
                !string.IsNullOrEmpty(src.SensorData)
                    ? JsonSerializer.Deserialize<Dictionary<string, double?>>(src.SensorData, (JsonSerializerOptions?)null)
                    : null));

        CreateMap<AirQualityData, AirQualityDataDto>();
        CreateMap<EnvironmentData, EnvironmentDataDto>();
    }

    private void ConfigureLogMappings()
    {
        CreateMap<LoginLog, LoginLogDto>()
            .ForMember(dest => dest.FailReason, opt => opt.MapFrom(src => src.FailureReason));

        CreateMap<OperationLog, OperationLogDto>();
    }
}
