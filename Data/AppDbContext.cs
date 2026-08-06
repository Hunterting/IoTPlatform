using IoTPlatform.Infrastructure.Tenant;
using IoTPlatform.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace IoTPlatform.Data;

/// <summary>
/// 标识需要租户隔离的实体接口
/// </summary>
public interface IHasAppCode
{
    string AppCode { get; set; }
}

/// <summary>
/// 应用数据库上下文
/// </summary>
public class AppDbContext : DbContext
{
    private readonly ITenantContextAccessor _tenantContextAccessor;

    public AppDbContext(DbContextOptions<AppDbContext> options,
        IServiceProvider serviceProvider) : base(options)
    {
        _tenantContextAccessor = serviceProvider.GetRequiredService<ITenantContextAccessor>();
    }

    // 用于设计时迁移
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
        _tenantContextAccessor = null!;
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
    public DbSet<AnShengDeviceConfig> AnShengDeviceConfigs { get; set; }
    public DbSet<DiscoveredAnShengDevice> DiscoveredAnShengDevices { get; set; }
    public DbSet<AnShengDeviceProfile> AnShengDeviceProfiles { get; set; }
    public DbSet<AnShengDeviceEvent> AnShengDeviceEvents { get; set; }
    public DbSet<AnShengCommandRecord> AnShengCommandRecords { get; set; }
    public DbSet<AnShengDelayTask> AnShengDelayTasks { get; set; }
    public DbSet<AnShengTimeTask> AnShengTimeTasks { get; set; }
    public DbSet<AnShengEmStatistic> AnShengEmStatistics { get; set; }
    public DbSet<DataRule> DataRules { get; set; }
    public DbSet<ETLTask> EtlTasks { get; set; }
    public DbSet<Gateway> Gateways { get; set; }
    public DbSet<Tunnel> Tunnels { get; set; }
    public DbSet<Plugin> Plugins { get; set; }
    public DbSet<DatabaseConfig> DatabaseConfigs { get; set; }

    // 设备指令
    public DbSet<DeviceCommand> DeviceCommands { get; set; }
    public DbSet<CommandHistory> CommandHistories { get; set; }

    // 受控设备（添加到指令控制系统的设备）
    public DbSet<ControlledDevice> ControlledDevices { get; set; }

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

    // 通用附件
    public DbSet<Models.Attachment> Attachments { get; set; }

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
        ConfigureAnShengDeviceConfigs(modelBuilder);
        ConfigureDiscoveredAnShengDevices(modelBuilder);
        ConfigureAnShengDeviceProfiles(modelBuilder);
        ConfigureAnShengDeviceEvents(modelBuilder);
        ConfigureAnShengCommandRecords(modelBuilder);
        ConfigureAnShengDelayTasks(modelBuilder);
        ConfigureAnShengTimeTasks(modelBuilder);
        ConfigureAnShengEmStatistics(modelBuilder);
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
        ConfigureGateways(modelBuilder);
        ConfigureTunnels(modelBuilder);
        ConfigurePlugins(modelBuilder);
        ConfigureDatabaseConfigs(modelBuilder);
        ConfigureDeviceCommands(modelBuilder);
        ConfigureCommandHistories(modelBuilder);
        ConfigureControlledDevices(modelBuilder);
        ConfigureAttachments(modelBuilder);
        // 配置全局租户过滤
        ConfigureGlobalQueryFilters(modelBuilder);

        // 配置软删除过滤器（如果需要）
        // ConfigureSoftDeleteFilters(modelBuilder);
    }

    /// <summary>
    /// 配置全局租户过滤
    /// </summary>
    private void ConfigureGlobalQueryFilters(ModelBuilder modelBuilder)
    {
        // 获取当前租户的 AppCode
        var appCode = _tenantContextAccessor?.Current?.AppCode;
        var isSuperAdmin = _tenantContextAccessor?.Current?.IsSuperAdmin ?? false;

        // 超级管理员不应用过滤
        if (isSuperAdmin)
        {
            return;
        }

        // 如果 AppCode 为空或未设置，不应用过滤（可能是在初始化阶段）
        if (string.IsNullOrEmpty(appCode))
        {
            return;
        }

        // 为所有实现 IHasAppCode 接口的实体添加查询过滤器
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (typeof(IHasAppCode).IsAssignableFrom(entityType.ClrType))
            {
                // 构建过滤器表达式
                var parameter = System.Linq.Expressions.Expression.Parameter(entityType.ClrType, "e");
                var property = System.Linq.Expressions.Expression.Property(parameter, nameof(IHasAppCode.AppCode));
                var value = System.Linq.Expressions.Expression.Constant(appCode);
                var equalsExpression = System.Linq.Expressions.Expression.Equal(property, value);

                // 应用过滤器
                modelBuilder.Entity(entityType.ClrType)
                    .HasQueryFilter(System.Linq.Expressions.Expression.Lambda(equalsExpression, parameter));
            }
        }
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
            entity.HasIndex(e => e.ProtocolConfigId);

            entity.Property(e => e.EnergyTypes).HasColumnType("json");

            entity.HasOne(e => e.ProtocolConfig)
                .WithMany()
                .HasForeignKey(e => e.ProtocolConfigId)
                .OnDelete(DeleteBehavior.SetNull);
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

    private void ConfigureAnShengDeviceConfigs(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AnShengDeviceConfig>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.DeviceId).IsUnique();
            entity.HasIndex(e => e.Imei);
            entity.HasIndex(e => e.AppCode);

            entity.HasOne(e => e.Device)
                .WithMany()
                .HasForeignKey(e => e.DeviceId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private void ConfigureDiscoveredAnShengDevices(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DiscoveredAnShengDevice>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Imei);
            entity.HasIndex(e => e.IsClaimed);
            entity.HasIndex(e => e.AppCode);

            // UNIQUE(Imei, AppCode)：待认领池的并发兜底。
            //
            // 【为什么必须有】设备发现走的是 check-then-act（查不到才插），而上行是并发的：
            //   connected 事件与首帧 getDevStatus 常落在同一毫秒级窗口，两条线程同时判定
            //   「不存在」后各插一行，同一设备在池子里出现两条待认领记录。
            //   （现场实证：IMEI=863434084755211 落出 Id=8/9 两行，DiscoveredAt 相差约 10ms。）
            //   应用层串行化只在单进程内有效，多实例部署时唯一键才是真正的最后一道防线。
            //
            // 【为什么带 AppCode】待认领池按租户隔离，同一 IMEI 在不同租户下各有一行是合法状态，
            //   唯一性只能约束到「租户内」。
            //
            // ⚠️ MySQL 语义提醒：唯一索引不约束 NULL —— AppCode 为 NULL 的行之间仍可重复。
            //   这一段由 AnShengDiscoveryService 内按 IMEI 的进程内串行化闸门补上。
            entity.HasIndex(e => new { e.Imei, e.AppCode }).IsUnique();

            // T5：枚举一律以 int 落库。
            // MySQL 5.7.26 下不用原生 ENUM——增枚举值就得 ALTER TABLE 锁表，
            // 且 Pomelo 对 ENUM 的映射在跨版本升级时并不稳定。
            entity.Property(e => e.Kind).HasConversion<int>();
            entity.Property(e => e.ProbeStatus).HasConversion<int>();

            // 待认领池列表页高频按「探测失败」筛选排障，单列索引足够。
            entity.HasIndex(e => e.ProbeStatus);
        });
    }

    /// <summary>
    /// 安圣设备能力档案表配置。
    ///
    /// 【索引设计理由】
    ///   · <c>UNIQUE(Imei)</c>：一台设备只能有一份档案，靠数据库兜底并发重复插入
    ///     （两个请求同时认领同一 IMEI 时，后者会撞唯一键而不是写出两份档案）。
    ///   · <c>(AppCode, Imei)</c> 普通索引：全局租户过滤器会给每条查询加
    ///     <c>WHERE AppCode = ?</c>，与 Imei 组成最常用的复合谓词。
    ///     不设成唯一——IMEI 全局唯一的语义比「租户内唯一」更强，跨租户重复即为数据事故。
    ///   · <c>DeviceId</c>：认领后按设备主键反查档案（能力校验主路径）。
    ///
    /// 【MySQL 5.7.26 约束】不使用 CHECK 约束（5.7 直接忽略，制造"以为有校验"的假象）、
    ///   不使用降序索引与函数索引（5.7 不支持）。
    /// </summary>
    private void ConfigureAnShengDeviceProfiles(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AnShengDeviceProfile>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.HasIndex(e => e.Imei).IsUnique();
            entity.HasIndex(e => new { e.AppCode, e.Imei });
            entity.HasIndex(e => e.DeviceId);

            entity.Property(e => e.Kind).HasConversion<int>();
            entity.Property(e => e.KindSource).HasConversion<int>();
            entity.Property(e => e.ProbeStatus).HasConversion<int>();

            // T8：插槽快照（设备权威回写落点）。
            //   SlotsSnapshot 存 int[] 的 JSON 文本，只存不查、不进索引，显式 longtext；
            //   SlotsSnapshotAt 时间列由 Pomelo 默认映射为 datetime(6)，存 UTC。
            entity.Property(e => e.SlotsSnapshot).HasColumnType("longtext");
        });
    }

    /// <summary>
    /// 安圣延时任务镜像表配置（T8）。
    ///
    /// 【索引设计理由】
    ///   · <c>UNIQUE(DeviceId, SlotNum)</c>：每设备每插槽一行，靠数据库兜底并发重复插入
    ///     （两次 startDelayTask 同一插槽时，后者会撞唯一键而不是写出两份）。
    ///   · <c>(AppCode, DeviceId)</c>：后台作用域用 <c>IgnoreQueryFilters</c> 时仍能走索引快速定位；
    ///     AppCode 打头是因为显式定位查询会带它（见 <see cref="Services.AnShengScheduleService"/>）。
    ///   · <c>DeviceId</c>：按设备反查全部插槽镜像（GET 端点主路径）。
    ///
    /// 【MySQL 5.7.26 约束】不使用 CHECK 约束、不使用函数索引；SAction/EAction 为 varchar 字符串
    ///   （无原生 ENUM）；时间列 datetime(6) 存 UTC。
    /// </summary>
    private void ConfigureAnShengDelayTasks(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AnShengDelayTask>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.HasIndex(e => new { e.DeviceId, e.SlotNum }).IsUnique();
            entity.HasIndex(e => new { e.AppCode, e.DeviceId });
            entity.HasIndex(e => e.DeviceId);

            entity.Property(e => e.AppCode).HasMaxLength(50).IsRequired();
            entity.Property(e => e.SAction).HasMaxLength(16).IsRequired();
            entity.Property(e => e.EAction).HasMaxLength(16).IsRequired();
        });
    }

    /// <summary>
    /// 安圣定时任务镜像配置（T10）。
    ///
    /// 【索引设计】唯一键 <c>(DeviceId, SlotNum, TaskKind, TaskIndex)</c>：定时任务每插槽挂
    ///   两个数组（timeTasks / loopTimeTasks），各自多项，故需 (SlotNum, TaskKind, TaskIndex)
    ///   三者才能唯一定位一行（区别于延时任务的 (DeviceId, SlotNum)）。
    ///   <c>AppCode</c> 打头索引供后台作用域按租户反查；<c>DeviceId</c> 单索引供 GET 主路径。
    ///
    /// 【乐观并发】<see cref="AnShengTimeTask.RowVersion"/> 声明为并发令牌（<c>IsConcurrencyToken</c>），
    ///   EF 生成的 UPDATE/DELETE 带 <c>AND RowVersion = @original</c>，两名管理员并发整表覆盖同一插槽时
    ///   后写者影响 0 行 → <c>DbUpdateConcurrencyException</c> → API 返回 409（验收 #5）。
    ///   该列是平台自增的 bigint，<b>不做</b>任何 <c>HasConversion</c>（默认的 int 映射即可，禁原生 ENUM/CHECK）。
    ///
    /// 【MySQL 5.7.26 约束】不使用 CHECK 约束、不使用函数索引；TaskKind 为 int 列（默认映射）。
    /// </summary>
    private void ConfigureAnShengTimeTasks(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AnShengTimeTask>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.HasIndex(e => new { e.DeviceId, e.SlotNum, e.TaskKind, e.TaskIndex }).IsUnique();
            entity.HasIndex(e => new { e.AppCode, e.DeviceId });
            entity.HasIndex(e => e.DeviceId);

            entity.Property(e => e.AppCode).HasMaxLength(50).IsRequired();
            entity.Property(e => e.TaskId).HasMaxLength(32);
            entity.Property(e => e.Action).HasMaxLength(16);
            entity.Property(e => e.WeekDays).HasMaxLength(64);

            // 乐观并发令牌：EF 在 UPDATE/DELETE 自动附带 RowVersion 比对（验收 #5）。
            entity.Property(e => e.RowVersion).IsConcurrencyToken();
        });
    }

    /// <summary>
    /// 安圣电量计统计聚合表配置（T11，设计 D5 Option C）。
    ///
    /// 【索引设计理由】
    ///   · <c>UNIQUE(DeviceId, SlotNum, Granularity, PeriodKey)</c>：这是本表的<b>生命线</b>（验收 #1）。
    ///     getEMStatistics 是全量快照式返回（dayData 最近 30 / monthData 最近 12），每拉一次就重复一次，
    ///     有唯一键才能做 UPSERT，没它重复拉取会把表撑成垃圾场。Granularity 单列不足以唯一定位
    ///     （Total / HourSum / Hour / Day / Month 同插槽可共存），故四元组才是真正的去重键。
    ///   · <c>(AppCode, DeviceId)</c>：后台作用域（Router 钩子）用 <c>IgnoreQueryFilters</c> 时仍能走索引
    ///     快速定位；AppCode 打头是因为显式定位查询会带它（见 <see cref="Services.AnShengEnergyService"/>）。
    ///   · <c>DeviceId</c> 单索引：按设备反查全部聚合行（GET 端点主路径）。
    ///
    /// 【MySQL 5.7.26 约束】不使用 CHECK 约束、不使用函数索引；Granularity 为 int 列（默认映射，
    /// 禁原生 ENUM）；PeriodKey 为 varchar(16) 且<b>非空</b>——唯一索引对 NULL 不去重，一旦允许 NULL，
    ///   Total 行就会无限重复插入（验收 #1 的杀手）。Kwh 为 double（默认映射）。
    /// </summary>
    private void ConfigureAnShengEmStatistics(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AnShengEmStatistic>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.HasIndex(e => new { e.DeviceId, e.SlotNum, e.Granularity, e.PeriodKey }).IsUnique();
            entity.HasIndex(e => new { e.AppCode, e.DeviceId });
            entity.HasIndex(e => e.DeviceId);

            entity.Property(e => e.AppCode).HasMaxLength(50).IsRequired();
            entity.Property(e => e.PeriodKey).HasMaxLength(AnShengEmStatistic.PeriodKeyMaxLength).IsRequired();
        });
    }

    /// <summary>
    /// 安圣设备事件溯源表配置（T6）。
    ///
    /// 【索引设计理由】
    ///   · <c>(Imei, OccurredAt)</c>：设备事件时间线，是本表<b>唯一的高频查询场景</b>
    ///     （运维打开某台设备详情页看「它都发生过什么」）。IMEI 在未认领时也存在，
    ///     所以它比 DeviceId 更适合做时间线主索引。
    ///   · <c>(DeviceId, OccurredAt)</c>：从平台设备维度反查（设备详情页已认领分支）。
    ///   · <c>(AppCode, Kind, OccurredAt)</c>：租户维度按事件类型统计。
    ///     AppCode 打头是因为 HTTP 侧查询会被全局租户过滤器自动加 <c>WHERE AppCode = ?</c>，
    ///     不打头则该谓词用不上索引。
    ///
    /// 【MySQL 5.7.26 约束】
    ///   · 枚举列一律 <c>int</c>（<c>HasConversion&lt;int&gt;()</c>），禁原生 ENUM；
    ///   · <b>不使用降序索引</b>——5.7 会静默忽略 DESC 关键字（只当普通索引建），
    ///     写了会给人「已按倒序优化」的错觉。倒序扫描由优化器自行处理，普通索引足够；
    ///   · 不使用 CHECK 约束（5.7 静默忽略）；
    ///   · <c>PayloadJson</c> / <c>RawJson</c> 显式声明 <c>longtext</c>，只存不查、不进索引；
    ///   · 时间列由 Pomelo 默认映射为 <c>datetime(6)</c>，存 UTC。
    ///
    /// 【不建分区表】5.7 的分区在外键与运维工具上限制颇多，当前也没有分区运维预案。
    ///   保留期由 <c>AnShengEventOptions.RetentionDays</c> 声明，清理作业见待办 W3。
    /// </summary>
    private void ConfigureAnShengDeviceEvents(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AnShengDeviceEvent>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.Property(e => e.AppCode).HasMaxLength(50).IsRequired();
            entity.Property(e => e.Imei).HasMaxLength(32).IsRequired();
            entity.Property(e => e.Method).HasMaxLength(32).IsRequired();
            entity.Property(e => e.FrameId).HasMaxLength(64);
            entity.Property(e => e.DispatchError).HasMaxLength(AnShengDeviceEvent.DispatchErrorMaxLength);

            entity.Property(e => e.Kind).HasConversion<int>();
            entity.Property(e => e.Severity).HasConversion<int>();

            entity.Property(e => e.PayloadJson).HasColumnType("longtext");
            entity.Property(e => e.RawJson).HasColumnType("longtext");

            entity.HasIndex(e => new { e.Imei, e.OccurredAt })
                .HasDatabaseName("IX_AnShengDeviceEvents_Imei_OccurredAt");
            entity.HasIndex(e => new { e.DeviceId, e.OccurredAt })
                .HasDatabaseName("IX_AnShengDeviceEvents_DeviceId_OccurredAt");
            entity.HasIndex(e => new { e.AppCode, e.Kind, e.OccurredAt })
                .HasDatabaseName("IX_AnShengDeviceEvents_AppCode_Kind_OccurredAt");
        });
    }

    /// <summary>
    /// 安圣命令记录表配置（T7 决策 D2：19 列 + 5 索引）。
    ///
    /// 【索引设计理由 —— 每一条都对应一个真实查询，没有「先建着以防万一」的索引】
    ///   · <c>UNIQUE(CommandId)</c>：对外主键。<c>GET /commands/{commandId}</c> 按它单点查询，
    ///     同时唯一约束就是<b>幂等护栏</b>——同一 CommandId 重复提交在 DB 层直接失败，
    ///     不必在应用层写「先查后插」的竞态代码。
    ///   · <c>(Imei, FrameId)</c> <b>非唯一</b>：应答关联主路径。
    ///     <b>刻意不设唯一</b>——frameId 只有 16 位 hex 且记录长期保留（默认 90 天），
    ///     同一 IMEI 跨月理论上会撞重。设成唯一会在生产上偶发写入失败，
    ///     而这类失败发生在「命令下发」这条最热的路径上，代价远大于收益。
    ///     关联时取 <c>Status IN (Pending, Sent)</c> 的最新一条即可。
    ///   · <c>(AppCode, IssuedAt)</c>：租户维度的命令列表/时间线。
    ///     AppCode 打头是因为 HTTP 侧查询会被全局租户过滤器自动加 <c>WHERE AppCode = ?</c>，
    ///     不打头则该谓词用不上索引（与 <c>ConfigureAnShengDeviceEvents</c> 同一理由）。
    ///   · <c>(DeviceId, IssuedAt)</c>：设备详情页的命令时间线（已认领设备分支）。
    ///   · <c>(Status, TimeoutAt)</c>：旁路清扫扫「未完成且已超时」。
    ///     Status 打头是因为它选择性最强——终态记录占绝大多数，
    ///     <c>Status IN (Pending, Sent)</c> 一步就能把扫描面收敛到在途区间。
    ///
    /// 【MySQL 5.7.26 约束】
    ///   · <c>Status</c> / <c>RejectReason</c> 一律 <c>int</c>（<c>HasConversion&lt;int&gt;()</c>），禁原生 ENUM；
    ///   · <b>不使用降序索引</b>——5.7 会静默忽略 DESC 关键字，写了只会给人「已按倒序优化」的错觉；
    ///   · 不使用 CHECK 约束（5.7 静默忽略）；状态机合法性由应用层保证；
    ///   · <c>DurationMs</c> <b>不用生成列</b>——5.7 生成列不能进函数索引，写入时算好存值；
    ///   · <c>RequestJson</c> / <c>ResponseJson</c> 显式声明 <c>longtext</c>，只存不查、<b>不进任何索引</b>；
    ///   · 时间列由 Pomelo 默认映射为 <c>datetime(6)</c>，存 UTC，禁 <c>timestamp</c>。
    ///
    /// 【为什么没有 Direction 列】本表定义是「<b>平台下发命令</b>的生命周期」，
    ///   上行报文已由 <c>AnShengDeviceEvent</c>（T6）承载。恒为 Downlink 的列既浪费存储，
    ///   又制造「两张表都能查上行」的错觉。将来若真需要上下行合并视图，用视图或联合查询。
    ///
    /// 【不建分区表】同事件表：5.7 的分区在外键与运维工具上限制颇多。
    ///   保留期由 <c>AnShengCommandOptions.RecordRetentionDays</c> 声明，清理作业见开放问题 U1。
    /// </summary>
    private void ConfigureAnShengCommandRecords(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AnShengCommandRecord>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.Property(e => e.AppCode).HasMaxLength(50).IsRequired();
            entity.Property(e => e.CommandId).HasMaxLength(36).IsRequired();
            entity.Property(e => e.Imei).HasMaxLength(32).IsRequired();
            entity.Property(e => e.Method).HasMaxLength(32).IsRequired();
            entity.Property(e => e.FrameId).HasMaxLength(64);
            entity.Property(e => e.ErrorCode).HasMaxLength(64);
            entity.Property(e => e.ErrorMessage).HasMaxLength(AnShengCommandRecord.ErrorMessageMaxLength);

            entity.Property(e => e.Status).HasConversion<int>();
            entity.Property(e => e.RejectReason).HasConversion<int>();

            entity.Property(e => e.RequestJson).HasColumnType("longtext").IsRequired();
            entity.Property(e => e.ResponseJson).HasColumnType("longtext");

            entity.HasIndex(e => e.CommandId)
                .IsUnique()
                .HasDatabaseName("IX_AnShengCommandRecords_CommandId");
            entity.HasIndex(e => new { e.Imei, e.FrameId })
                .HasDatabaseName("IX_AnShengCommandRecords_Imei_FrameId");
            entity.HasIndex(e => new { e.AppCode, e.IssuedAt })
                .HasDatabaseName("IX_AnShengCommandRecords_AppCode_IssuedAt");
            entity.HasIndex(e => new { e.DeviceId, e.IssuedAt })
                .HasDatabaseName("IX_AnShengCommandRecords_DeviceId_IssuedAt");
            entity.HasIndex(e => new { e.Status, e.TimeoutAt })
                .HasDatabaseName("IX_AnShengCommandRecords_Status_TimeoutAt");
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

    private void ConfigureDeviceCommands(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DeviceCommand>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.CommandId).IsUnique();
            entity.HasIndex(e => e.DeviceId);
            entity.HasIndex(e => e.AppCode);
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.CreatedAt);

            entity.HasOne(e => e.Device)
                .WithMany()
                .HasForeignKey(e => e.DeviceId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private void ConfigureCommandHistories(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CommandHistory>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.CommandId);
            entity.HasIndex(e => e.AppCode);
            entity.HasIndex(e => e.CreatedAt);

            entity.HasOne(e => e.Command)
                .WithMany()
                .HasForeignKey(e => e.CommandId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private void ConfigureControlledDevices(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ControlledDevice>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.AppCode);
            entity.HasIndex(e => e.DeviceId).IsUnique(); // 每个设备只能注册一次
            entity.HasIndex(e => e.IsEnabled);
            entity.HasIndex(e => e.IsFavorite);
            entity.HasIndex(e => e.Priority);
            entity.HasIndex(e => e.RegisteredAt);

            entity.HasOne(e => e.Device)
                .WithMany()
                .HasForeignKey(e => e.DeviceId)
                .OnDelete(DeleteBehavior.Cascade);
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

    private void ConfigureAttachments(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Models.Attachment>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Module);
            entity.HasIndex(e => e.BusinessId);
            entity.HasIndex(e => e.AppCode);
            entity.HasIndex(e => e.UploadDate);
        });
    }

    private void ConfigureGateways(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Gateway>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.AppCode);
            entity.HasIndex(e => e.SourceProtocol);
            entity.HasIndex(e => e.TargetProtocol);
            entity.HasIndex(e => e.Status);
        });
    }

    private void ConfigureTunnels(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Tunnel>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.AppCode);
            entity.HasIndex(e => e.TunnelType);
            entity.HasIndex(e => e.Status);
        });
    }

    private void ConfigurePlugins(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Plugin>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.AppCode);
            entity.HasIndex(e => e.PluginType);
            entity.HasIndex(e => e.Status);
        });
    }

    private void ConfigureDatabaseConfigs(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DatabaseConfig>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.AppCode);
            entity.HasIndex(e => e.DatabaseType);
            entity.HasIndex(e => e.Status);
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
