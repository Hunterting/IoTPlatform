# 服务层仓储模式迁移指南

## 概述

本文档指导如何将现有的服务层代码从直接使用DbContext迁移到使用仓储模式。仓储模式提供了更好的数据访问抽象、代码重用和测试支持。

## 已完成的工作

1. **仓储接口**：已创建了所有主要实体的仓储接口
2. **仓储实现**：已创建了关键实体的仓储实现类
3. **单元工作模式**：实现了IUnitOfWork接口
4. **AutoMapper配置**：创建了实体到DTO的映射配置
5. **种子数据**：实现了初始化种子数据
6. **服务注册**：在Program.cs中注册了所有服务

## 服务层迁移步骤

### 1. 修改服务类构造函数

从直接注入DbContext改为注入对应的仓储接口：

**之前：**
```csharp
public class UserService : IUserService
{
    private readonly AppDbContext _context;
    private readonly IMapper _mapper;

    public UserService(AppDbContext context, IMapper mapper)
    {
        _context = context;
        _mapper = mapper;
    }
}
```

**之后：**
```csharp
public class UserService : IUserService
{
    private readonly IUserRepository _userRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMapper _mapper;

    public UserService(
        IUserRepository userRepository,
        IUnitOfWork unitOfWork,
        IMapper mapper)
    {
        _userRepository = userRepository;
        _unitOfWork = unitOfWork;
        _mapper = mapper;
    }
}
```

### 2. 更新数据访问方法

将直接使用_context.Users改为使用仓储方法：

**之前：**
```csharp
public async Task<UserDto> GetUserAsync(long id)
{
    var user = await _context.Users.FindAsync(id);
    return _mapper.Map<UserDto>(user);
}

public async Task<UserDto> CreateUserAsync(CreateUserRequest request)
{
    var user = _mapper.Map<User>(request);
    await _context.Users.AddAsync(user);
    await _context.SaveChangesAsync();
    return _mapper.Map<UserDto>(user);
}
```

**之后：**
```csharp
public async Task<UserDto> GetUserAsync(long id)
{
    var user = await _userRepository.GetByIdAsync(id);
    return _mapper.Map<UserDto>(user);
}

public async Task<UserDto> CreateUserAsync(CreateUserRequest request)
{
    var user = _mapper.Map<User>(request);
    var createdUser = await _userRepository.AddAsync(user);
    await _unitOfWork.SaveChangesAsync();
    return _mapper.Map<UserDto>(createdUser);
}
```

### 3. 使用AutoMapper扩展方法

利用已创建的AutoMapper扩展方法简化映射：

**示例：**
```csharp
// 查询并映射到DTO
public async Task<IEnumerable<UserDto>> GetUsersByAppCodeAsync(string appCode)
{
    var users = await _userRepository.GetByAppCodeAsync(appCode);
    return _mapper.Map<IEnumerable<UserDto>>(users);
}

// 分页查询
public async Task<PagedResult<UserDto>> GetPagedUsersAsync(int page, int pageSize)
{
    var pagedUsers = await _userRepository.GetPagedAsync(page, pageSize);
    return _mapper.Map<PagedResult<UserDto>>(pagedUsers);
}
```

### 4. 处理事务

使用UnitOfWork进行事务管理：

**之前：**
```csharp
public async Task<bool> TransferUserAsync(long fromUserId, long toUserId)
{
    using var transaction = await _context.Database.BeginTransactionAsync();
    try
    {
        var fromUser = await _context.Users.FindAsync(fromUserId);
        var toUser = await _context.Users.FindAsync(toUserId);
        
        // 业务逻辑...
        
        await _context.SaveChangesAsync();
        await transaction.CommitAsync();
        return true;
    }
    catch
    {
        await transaction.RollbackAsync();
        throw;
    }
}
```

**之后：**
```csharp
public async Task<bool> TransferUserAsync(long fromUserId, long toUserId)
{
    await _unitOfWork.BeginTransactionAsync();
    try
    {
        var fromUser = await _userRepository.GetByIdAsync(fromUserId);
        var toUser = await _userRepository.GetByIdAsync(toUserId);
        
        // 业务逻辑...
        
        await _unitOfWork.SaveChangesAsync();
        await _unitOfWork.CommitTransactionAsync();
        return true;
    }
    catch
    {
        await _unitOfWork.RollbackTransactionAsync();
        throw;
    }
}
```

## 需要更新的服务类

以下是项目中需要迁移的服务类列表：

### 认证和用户管理
- `AuthService` - 使用IUserRepository, IRoleRepository
- `UserService` - 使用IUserRepository
- `RoleService` - 使用IRoleRepository

### 客户和项目管理
- `CustomerService` - 使用ICustomerRepository
- `ProjectService` - 使用IProjectRepository

### 设备和区域管理
- `AreaService` - 使用IAreaRepository
- `DeviceService` - 使用IDeviceRepository
- `MonitoringService` - 使用IMonitoringRepository

### 告警和工单管理
- `AlertService` - 使用IAlertRecordRepository
- `WorkOrderService` - 使用IWorkOrderRepository

### 辅助功能
- `ArchiveService` - 使用IArchiveRepository
- `LogService` - 使用ILogRepository
- `DictionaryService` - 使用IDictionaryRepository
- `SettingsService` - 使用ISystemSettingRepository

### 高级功能
- `ProtocolConfigService` - 使用IProtocolConfigRepository
- `DataRuleService` - 使用IDataRuleRepository
- `ETLTaskService` - 使用IETLTaskRepository
- `DataCollectionService` - 使用IMonitoringRepository, IDeviceRepository

## 迁移优先级建议

1. **高优先级**：AuthService, UserService, RoleService（认证相关）
2. **中优先级**：CustomerService, ProjectService（租户相关）
3. **中优先级**：AreaService, DeviceService（核心业务）
4. **低优先级**：其他服务类

## 测试建议

迁移后需要测试：
1. 确保所有API端点正常工作
2. 测试分页查询功能
3. 测试事务管理功能
4. 测试多租户数据隔离
5. 测试区域权限过滤

## 回滚策略

如果迁移过程中出现问题：
1. 恢复服务类为使用DbContext的版本
2. 注释掉Program.cs中的仓储服务注册
3. 重新使用DbContext注入

## 下一步工作

完成服务层迁移后，可以进行以下优化：
1. 添加仓储层缓存支持
2. 实现更复杂的分页查询
3. 添加查询规范模式
4. 实现软删除的全局过滤器

## 联系方式

如有问题，请参考项目文档或联系开发团队。