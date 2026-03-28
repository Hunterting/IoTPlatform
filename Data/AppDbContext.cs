using IoTPlatform.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;

namespace IoTPlatform.Data;

/// <summary>
/// 应用数据库上下文
/// </summary>
public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    // 用户和认证
    public DbSet<User> Users { get; set; }
    public DbSet<Role> Roles { get; set; }

    // 租户和项目
    public DbSet<Customer> Customers { get; set; }
    public DbSet<Project> Projects { get; set; }
    public DbSet<Contract> Contracts { get; set; }
    public DbSet<WorkSummary> WorkSummaries { get; set; }

    // 区域和设备
    public DbSet<Area> Areas { get; set; }
    public DbSet<Device> Devices { get; set; }
    public DbSet<DeviceSensor> DeviceSensors { get; set; }
    public DbSet<DeviceDataRecord> DeviceDataRecords { get; set; }
    public DbSet<AreaDevice> AreaDevices { get; set; }

    // 告警和工单
    public DbSet<AlertRecord> AlertRecords { get; set; }
    public DbSet<AlertProcessLog> AlertProcessLogs { get; set; }
    public DbSet<WorkOrder> WorkOrders { get; set; }
    public DbSet<WorkOrderLog> WorkOrderLogs { get; set; }
    public DbSet<WorkOrderAttachment> WorkOrderAttachments { get; set; }

    // 档案和相机
    public DbSet<Archive> Archives { get; set; }
    public DbSet<ArchiveDeviceMarker> ArchiveDeviceMarkers { get; set; }
    public DbSet<Camera> Cameras { get; set; }

    // 数据采集
    public DbSet<ProtocolConfig> ProtocolConfigs { get; set; }
    public DbSet<DataRule> DataRules { get; set; }
    public DbSet<ETLTask> EtlTasks { get; set; }

        // 日志和字典
        public DbSet<LoginLog> LoginLogs { get; set; }
        public DbSet<OperationLog> OperationLogs { get; set; }
        public DbSet<DictionaryItem> DictionaryItems { get; set; }
        public DbSet<DictionaryTypeConfig> DictionaryTypeConfigs { get; set; }

    // 监控数据
    public DbSet<AirQualityData> AirQualityData { get; set; }
    public DbSet<EnvironmentData> EnvironmentData { get; set; }

    // 系统设置
    public DbSet<SystemSetting> SystemSettings { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // 配置表名和索引
        ConfigureUsers(modelBuilder);
        ConfigureRoles(modelBuilder);
        ConfigureCustomers(modelBuilder);
        ConfigureProjects(modelBuilder);
        ConfigureAreas(modelBuilder);
        ConfigureDevices(modelBuilder);
        ConfigureAlertRecords(modelBuilder);
        ConfigureWorkOrders(modelBuilder);
        ConfigureAlertProcessLogs(modelBuilder);
        ConfigureWorkOrderLogs(modelBuilder);
        ConfigureWorkOrderAttachments(modelBuilder);
        ConfigureArchives(modelBuilder);
        ConfigureArchiveDeviceMarkers(modelBuilder);
        ConfigureCameras(modelBuilder);
        ConfigureProtocolConfigs(modelBuilder);
        ConfigureLoginLogs(modelBuilder);
        ConfigureOperationLogs(modelBuilder);
        ConfigureDictionaryItems(modelBuilder);
        ConfigureDictionaryTypes(modelBuilder);
        ConfigureAirQualityData(modelBuilder);
        ConfigureEnvironmentData(modelBuilder);
        ConfigureContracts(modelBuilder);
        ConfigureWorkSummaries(modelBuilder);
        ConfigureDeviceSensors(modelBuilder);
        ConfigureDeviceDataRecords(modelBuilder);
        ConfigureAreaDevices(modelBuilder);
        ConfigureDataRules(modelBuilder);
        ConfigureEtlTasks(modelBuilder);
        ConfigureSystemSettings(modelBuilder);

        // 配置软删除过滤器（如果需要）
        // ConfigureGlobalQueryFilters(modelBuilder);
    }

    private void ConfigureUsers(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Email).IsUnique();
            entity.HasIndex(e => e.CustomerId);
            entity.HasIndex(e => e.Role);
            entity.HasIndex(e => e.AppCode);
            entity.HasIndex(e => e.IsActive);

            entity.Property(e => e.EnergyTypes).HasColumnType("json");
        });
    }

    private void ConfigureRoles(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Role>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Code).IsUnique();
            entity.HasIndex(e => e.AppCode);
        });
    }

    private void ConfigureCustomers(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Customer>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Code).IsUnique();
            entity.HasIndex(e => e.AppCode).IsUnique();
            entity.HasIndex(e => e.Status);
        });
    }

    private void ConfigureProjects(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Project>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.CustomerId);
            entity.HasIndex(e => e.AppCode);
            entity.HasIndex(e => e.Status);
        });
    }

    private void ConfigureAreas(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Area>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.ParentId);
            entity.HasIndex(e => e.CustomerId);
            entity.HasIndex(e => e.AppCode);
            entity.HasIndex(e => e.Type);

            entity.HasOne(e => e.Parent)
                .WithMany(e => e.Children)
                .HasForeignKey(e => e.ParentId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }

    private void ConfigureDevices(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Device>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.AppCode);
            entity.HasIndex(e => e.AreaId);
            entity.HasIndex(e => e.ProjectId);
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.SerialNumber);

            entity.Property(e => e.EnergyTypes).HasColumnType("json");
        });
    }

    private void ConfigureAlertRecords(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AlertRecord>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.AlertNo).IsUnique();
            entity.HasIndex(e => e.DeviceId);
            entity.HasIndex(e => e.AreaId);
            entity.HasIndex(e => e.AppCode);
            entity.HasIndex(e => e.AlertType);
            entity.HasIndex(e => e.Level);
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.AlertTime);
        });
    }

    private void ConfigureAlertProcessLogs(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AlertProcessLog>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.AlertRecordId);
            entity.HasIndex(e => e.ProcessedBy);

            entity.HasOne(e => e.AlertRecord)
                .WithMany(e => e.ProcessLogs)
                .HasForeignKey(e => e.AlertRecordId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private void ConfigureWorkOrders(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<WorkOrder>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.OrderNo).IsUnique();
            entity.HasIndex(e => e.CustomerId);
            entity.HasIndex(e => e.DeviceId);
            entity.HasIndex(e => e.AreaId);
            entity.HasIndex(e => e.AppCode);
            entity.HasIndex(e => e.Type);
            entity.HasIndex(e => e.Priority);
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.ReportTime);
        });
    }

    private void ConfigureWorkOrderLogs(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<WorkOrderLog>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.WorkOrderId);

            entity.HasOne(e => e.WorkOrder)
                .WithMany(e => e.Logs)
                .HasForeignKey(e => e.WorkOrderId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private void ConfigureWorkOrderAttachments(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<WorkOrderAttachment>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.WorkOrderId);

            entity.HasOne(e => e.WorkOrder)
                .WithMany(e => e.Attachments)
                .HasForeignKey(e => e.WorkOrderId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private void ConfigureArchives(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Archive>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.ProjectId);
            entity.HasIndex(e => e.AppCode);
            entity.HasIndex(e => e.Type);
        });
    }

    private void ConfigureArchiveDeviceMarkers(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ArchiveDeviceMarker>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.ArchiveId);
            entity.HasIndex(e => e.DeviceId);

            entity.HasOne(e => e.Archive)
                .WithMany(e => e.DeviceMarkers)
                .HasForeignKey(e => e.ArchiveId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private void ConfigureCameras(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Camera>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.AreaId);
            entity.HasIndex(e => e.AppCode);
        });
    }

    private void ConfigureProtocolConfigs(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ProtocolConfig>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.AppCode);
            entity.HasIndex(e => e.ProtocolType);
            entity.HasIndex(e => e.IsActive);
        });
    }

    private void ConfigureLoginLogs(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<LoginLog>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => e.Email);
            entity.HasIndex(e => e.LoginTime);
        });
    }

    private void ConfigureOperationLogs(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OperationLog>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => e.Module);
            entity.HasIndex(e => e.Operation);
            entity.HasIndex(e => e.OperationTime);
        });
    }

    private void ConfigureDictionaryItems(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DictionaryItem>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.TypeCode);
            entity.HasIndex(e => e.Code);
            entity.HasIndex(e => e.AppCode);
        });
    }

    private void ConfigureAirQualityData(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AirQualityData>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.DeviceId);
            entity.HasIndex(e => e.AreaId);
            entity.HasIndex(e => e.AppCode);
            entity.HasIndex(e => e.RecordTime);
        });
    }

    private void ConfigureEnvironmentData(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<EnvironmentData>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.DeviceId);
            entity.HasIndex(e => e.AreaId);
            entity.HasIndex(e => e.AppCode);
            entity.HasIndex(e => e.RecordTime);
        });
    }

    private void ConfigureContracts(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Contract>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.ProjectId);
            entity.HasIndex(e => e.AppCode);

            entity.HasOne(e => e.Project)
                .WithMany(e => e.Contracts)
                .HasForeignKey(e => e.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private void ConfigureWorkSummaries(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<WorkSummary>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.ProjectId);
            entity.HasIndex(e => e.Date);
            entity.HasIndex(e => e.AppCode);

            entity.HasOne(e => e.Project)
                .WithMany(e => e.WorkSummaries)
                .HasForeignKey(e => e.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private void ConfigureDeviceSensors(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DeviceSensor>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.DeviceId);

            entity.HasOne(e => e.Device)
                .WithMany(e => e.Sensors)
                .HasForeignKey(e => e.DeviceId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private void ConfigureDeviceDataRecords(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DeviceDataRecord>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.DeviceId);
            entity.HasIndex(e => e.RecordTime);

            entity.HasOne(e => e.Device)
                .WithMany(e => e.DataRecords)
                .HasForeignKey(e => e.DeviceId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private void ConfigureAreaDevices(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AreaDevice>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.AreaId);
            entity.HasIndex(e => e.DeviceId);

            entity.HasOne(e => e.Area)
                .WithMany(e => e.Devices)
                .HasForeignKey(e => e.AreaId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private void ConfigureDataRules(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DataRule>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.DeviceId);
            entity.HasIndex(e => e.AreaId);
            entity.HasIndex(e => e.AppCode);
            entity.HasIndex(e => e.RuleType);
            entity.HasIndex(e => e.IsActive);
        });
    }

    private void ConfigureEtlTasks(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ETLTask>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.TaskType);
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.AppCode);
        });
    }

    private void ConfigureDictionaryTypes(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DictionaryTypeConfig>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Code).IsUnique();
            entity.HasIndex(e => e.AppCode);
            entity.HasIndex(e => e.IsActive);
        });
    }

    private void ConfigureSystemSettings(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SystemSetting>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Key).IsUnique();
            entity.HasIndex(e => e.Category);
            entity.HasIndex(e => e.AppCode);
        });
    }

        /// <summary>
        /// 初始化种子数据
        /// </summary>
        public async Task SeedDataAsync(IServiceProvider serviceProvider)
        {
            try
            {
                var logger = serviceProvider.GetRequiredService<ILogger<AppDbContext>>();
                logger.LogInformation("开始初始化数据库种子数据...");

                // 检查数据库是否已存在数据
                var hasData = await this.Users.AnyAsync();
                if (hasData)
                {
                    logger.LogInformation("数据库中已有数据，跳过种子数据初始化");
                    return;
                }

                // 初始化种子数据
                var dataSeederLogger = serviceProvider.GetRequiredService<ILogger<IoTPlatform.Data.SeedData.DataSeeder>>();
                var seeder = new IoTPlatform.Data.SeedData.DataSeeder(serviceProvider, dataSeederLogger);
                await seeder.InitializeAllAsync();

                logger.LogInformation("数据库种子数据初始化完成");
            }
            catch (Exception ex)
            {
                var logger = serviceProvider.GetRequiredService<ILogger<AppDbContext>>();
                logger.LogError(ex, "初始化种子数据时发生错误");
                throw;
            }
        }

        /// <summary>
        /// 开发环境专用种子数据初始化
        /// </summary>
        public async Task SeedDataForDevelopmentAsync(IServiceProvider serviceProvider)
        {
            try
            {
                var logger = serviceProvider.GetRequiredService<ILogger<AppDbContext>>();
                logger.LogInformation("开始初始化开发环境数据库种子数据...");

                // 开发环境：清空数据库并重新初始化
                var dataSeederLogger = serviceProvider.GetRequiredService<ILogger<IoTPlatform.Data.SeedData.DataSeeder>>();
                var seeder = new IoTPlatform.Data.SeedData.DataSeeder(serviceProvider, dataSeederLogger);
                await seeder.InitializeForDevelopmentAsync();

                logger.LogInformation("开发环境数据库种子数据初始化完成");
            }
            catch (Exception ex)
            {
                var logger = serviceProvider.GetRequiredService<ILogger<AppDbContext>>();
                logger.LogError(ex, "初始化开发环境种子数据时发生错误");
                throw;
            }
        }
    }
