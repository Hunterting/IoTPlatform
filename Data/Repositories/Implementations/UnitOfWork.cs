using IoTPlatform.Data.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore.Storage;

namespace IoTPlatform.Data.Repositories.Implementations;

/// <summary>
/// 单元工作实现类
/// </summary>
public class UnitOfWork : IUnitOfWork
{
    private readonly AppDbContext _context;
    private IDbContextTransaction? _transaction;
    private bool _disposed;

    // 仓储属性
    private IUserRepository? _userRepository;
    private IRoleRepository? _roleRepository;
    private ICustomerRepository? _customerRepository;
    private IProjectRepository? _projectRepository;
    private IAreaRepository? _areaRepository;
    private IDeviceRepository? _deviceRepository;
    private IAlertRecordRepository? _alertRecordRepository;
    private IWorkOrderRepository? _workOrderRepository;
    private IArchiveRepository? _archiveRepository;
    private IProtocolConfigRepository? _protocolConfigRepository;
    private IDataRuleRepository? _dataRuleRepository;
    private IDictionaryRepository? _dictionaryRepository;
    private ISystemSettingRepository? _systemSettingRepository;
    private IETLTaskRepository? _etlTaskRepository;
    // private ILogRepository? _logRepository;
    // private IMonitoringRepository? _monitoringRepository;

    public UnitOfWork(AppDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public IRepository<T> GetRepository<T>() where T : class
    {
        return new Repository<T>(_context);
    }

    public IUserRepository UserRepository => _userRepository ??= new UserRepository(_context);
    public IRoleRepository RoleRepository => _roleRepository ??= new RoleRepository(_context);
    public ICustomerRepository CustomerRepository => _customerRepository ??= new CustomerRepository(_context);
    public IProjectRepository ProjectRepository => _projectRepository ??= new ProjectRepository(_context);
    public IAreaRepository AreaRepository => _areaRepository ??= new AreaRepository(_context);
    public IDeviceRepository DeviceRepository => _deviceRepository ??= new DeviceRepository(_context);
    public IAlertRecordRepository AlertRecordRepository => _alertRecordRepository ??= new AlertRecordRepository(_context);
    public IWorkOrderRepository WorkOrderRepository => _workOrderRepository ??= new WorkOrderRepository(_context);
    public IArchiveRepository ArchiveRepository => _archiveRepository ??= new ArchiveRepository(_context);
    public IProtocolConfigRepository ProtocolConfigRepository => _protocolConfigRepository ??= new ProtocolConfigRepository(_context);
    public IDataRuleRepository DataRuleRepository => _dataRuleRepository ??= new DataRuleRepository(_context);
    public IDictionaryRepository DictionaryRepository => _dictionaryRepository ??= new DictionaryRepository(_context);
    public ISystemSettingRepository SystemSettingRepository => _systemSettingRepository ??= new SystemSettingRepository(_context);
    public IETLTaskRepository ETLTaskRepository => _etlTaskRepository ??= new ETLTaskRepository(_context);
    // public ILogRepository LogRepository => _logRepository ??= new LogRepository(_context);
    // public IMonitoringRepository MonitoringRepository => _monitoringRepository ??= new MonitoringRepository(_context);

    public async Task<int> SaveChangesAsync()
    {
        return await _context.SaveChangesAsync();
    }

    public async Task BeginTransactionAsync()
    {
        if (_transaction != null)
        {
            throw new InvalidOperationException("事务已经存在");
        }
        
        _transaction = await _context.Database.BeginTransactionAsync();
    }

    public async Task CommitTransactionAsync()
    {
        if (_transaction == null)
        {
            throw new InvalidOperationException("没有活动的事务");
        }

        try
        {
            await _context.SaveChangesAsync();
            await _transaction.CommitAsync();
        }
        finally
        {
            await _transaction.DisposeAsync();
            _transaction = null;
        }
    }

    public async Task CommitAsync()
    {
        await CommitTransactionAsync();
    }

    public async Task RollbackTransactionAsync()
    {
        if (_transaction == null)
        {
            throw new InvalidOperationException("没有活动的事务");
        }

        try
        {
            await _transaction.RollbackAsync();
        }
        finally
        {
            await _transaction.DisposeAsync();
            _transaction = null;
        }
    }

    public async Task RollbackAsync()
    {
        await RollbackTransactionAsync();
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                _context?.Dispose();
                _transaction?.Dispose();
            }
            
            _disposed = true;
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    ~UnitOfWork()
    {
        Dispose(false);
    }
}
