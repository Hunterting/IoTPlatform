namespace IoTPlatform.Data.Repositories.Interfaces;

/// <summary>
/// 单元工作接口
/// </summary>
public interface IUnitOfWork : IDisposable
{
    /// <summary>
    /// 获取指定实体类型的仓储
    /// </summary>
    /// <typeparam name="T">实体类型</typeparam>
    /// <returns>实体仓储</returns>
    IRepository<T> GetRepository<T>() where T : class;
    
    /// <summary>
    /// 保存所有更改
    /// </summary>
    /// <returns>受影响的行数</returns>
    Task<int> SaveChangesAsync();
    
    /// <summary>
    /// 开始事务
    /// </summary>
    Task BeginTransactionAsync();
    
    /// <summary>
    /// 提交事务
    /// </summary>
    Task CommitTransactionAsync();

    /// <summary>
    /// 提交事务（别名方法）
    /// </summary>
    Task CommitAsync();

    /// <summary>
    /// 回滚事务
    /// </summary>
    Task RollbackTransactionAsync();

    /// <summary>
    /// 回滚事务（别名方法）
    /// </summary>
    Task RollbackAsync();
    
    /// <summary>
    /// 获取用户仓储
    /// </summary>
    IUserRepository UserRepository { get; }
    
    /// <summary>
    /// 获取角色仓储
    /// </summary>
    IRoleRepository RoleRepository { get; }
    
    /// <summary>
    /// 获取客户仓储
    /// </summary>
    ICustomerRepository CustomerRepository { get; }
    
    /// <summary>
    /// 获取项目仓储
    /// </summary>
    IProjectRepository ProjectRepository { get; }
    
    /// <summary>
    /// 获取区域仓储
    /// </summary>
    IAreaRepository AreaRepository { get; }
    
    /// <summary>
    /// 获取设备仓储
    /// </summary>
    IDeviceRepository DeviceRepository { get; }
    
    /// <summary>
    /// 获取告警仓储
    /// </summary>
    IAlertRecordRepository AlertRecordRepository { get; }
    
    /// <summary>
    /// 获取工单仓储
    /// </summary>
    IWorkOrderRepository WorkOrderRepository { get; }
    
    /// <summary>
    /// 获取档案仓储
    /// </summary>
    IArchiveRepository ArchiveRepository { get; }
    
    /// <summary>
    /// 获取协议配置仓储
    /// </summary>
    IProtocolConfigRepository ProtocolConfigRepository { get; }
    
    /// <summary>
    /// 获取数据规则仓储
    /// </summary>
    IDataRuleRepository DataRuleRepository { get; }
    
    /// <summary>
    /// 获取字典仓储
    /// </summary>
    IDictionaryRepository DictionaryRepository { get; }
    
    /// <summary>
    /// 获取系统设置仓储
    /// </summary>
    ISystemSettingRepository SystemSettingRepository { get; }
    
    /// <summary>
    /// 获取ETL任务仓储
    /// </summary>
    IETLTaskRepository ETLTaskRepository { get; }

    /// <summary>
    /// 获取日志仓储
    /// </summary>
    // ILogRepository LogRepository { get; }

    /// <summary>
    /// 获取监控数据仓储
    /// </summary>
    // IMonitoringRepository MonitoringRepository { get; }
}
