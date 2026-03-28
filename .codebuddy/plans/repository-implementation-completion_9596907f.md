---
name: repository-implementation-completion
overview: 为4个Repository类补充缺少的接口方法实现，包括DataRuleRepository、DictionaryRepository、ETLTaskRepository和SystemSettingRepository。
todos:
  - id: implement-data-rule-repo
    content: 实现DataRuleRepository缺少的3个方法(UpdateStatusAsync, GetDataRuleStatsAsync, GetApplicableRulesAsync)
    status: completed
  - id: implement-dictionary-repo
    content: 实现DictionaryRepository缺少的9个方法
    status: completed
  - id: implement-etl-task-repo
    content: 实现ETLTaskRepository缺少的8个方法
    status: completed
  - id: implement-system-setting-repo
    content: 实现SystemSettingRepository缺少的8个方法
    status: completed
  - id: verify-compilation
    content: 验证编译通过并检查剩余错误
    status: completed
    dependencies:
      - implement-data-rule-repo
      - implement-dictionary-repo
      - implement-etl-task-repo
      - implement-system-setting-repo
---

## 用户需求

实现Repository层缺少的接口方法,共28个方法分布在4个Repository类中:

### DataRuleRepository (3个方法)

1. UpdateStatusAsync - 更新规则状态
2. GetDataRuleStatsAsync - 获取数据规则统计信息(返回类型需修正)
3. GetApplicableRulesAsync - 获取适用于指定设备的规则列表

### DictionaryRepository (9个方法)

1. GetByTypeCodeAsync - 根据字典类型代码获取字典项列表
2. GetByTypeAndCodeAsync - 根据字典项代码获取字典项
3. GetByStatusAsync - 根据字典项状态获取字典项列表
4. TypeCodeExistsAsync - 检查字典类型代码是否已存在
5. ItemCodeExistsAsync - 检查字典项代码是否已存在
6. GetActiveItemsByTypeAsync - 获取活动的字典项列表
7. UpdateItemStatusAsync - 更新字典项状态
8. UpdateTypeStatusAsync - 更新字典类型状态
9. GetDictionaryStatsAsync - 获取字典统计信息(返回类型需修正)

### ETLTaskRepository (8个方法)

1. GetByTaskTypeAsync - 根据任务类型获取任务列表
2. GetByStatusAsync - 根据任务状态获取任务列表(方法签名需修正)
3. GetTasksToRunAsync - 获取需要运行的任务列表
4. UpdateTaskStatusAsync - 更新任务状态(方法签名需修正)
5. NameExistsAsync - 检查ETL任务名称是否已存在
6. GetExecutionHistoryAsync - 获取任务执行历史
7. RecordExecutionHistoryAsync - 记录任务执行历史
8. GetETLTaskStatsAsync - 获取ETL任务统计信息

### SystemSettingRepository (8个方法)

1. GetByKeyAsync - 根据设置键获取设置(需要统一重载)
2. GetByCategoryAsync - 根据设置分类获取设置列表
3. KeyExistsAsync - 检查设置键是否已存在
4. GetOrCreateAsync - 获取或创建设置(如果不存在)
5. GetValueAsync<T> - 获取设置值(强类型)
6. SetValueAsync<T> - 设置值
7. GetValuesAsync - 批量获取设置值
8. SetValuesAsync - 批量设置值
9. GetCategoriesAsync - 获取所有设置分类

## 核心功能

补全Repository层的接口方法实现,消除编译错误,确保所有Repository实现类完整实现其对应的接口定义。

## 关键约束

- 必须遵循现有Repository模式和代码风格
- 使用Entity Framework Core和LINQ查询
- 保持与现有实现的一致性
- 方法签名必须与接口定义完全匹配
- 返回类型必须符合接口定义

## 技术栈

- 后端框架: .NET 8.0 (C#)
- ORM框架: Entity Framework Core
- 数据库上下文: AppDbContext
- 设计模式: Repository Pattern + Unit of Work Pattern

## 技术架构

### Repository模式

- 基类: Repository<T> 提供通用CRUD操作
- 接口继承: 所有特定Repository接口继承自IRepository<T>
- 实现继承: 实现类继承自Repository<T>并实现特定接口
- 过滤器模式: 使用ApplyFilters方法统一处理appCode和customerId过滤

### 数据访问层

```
Controller → Service → Repository → DbContext → Database
```

### 实现策略

1. 方法签名严格匹配接口定义
2. 使用_dbSet或_context访问数据集
3. 应用统一的过滤器(AppCode过滤)
4. 适当的Include加载导航属性
5. 统计方法返回完整的统计对象而非元组

## 实现细节

### DataRuleRepository

- UpdateStatusAsync: 与现有UpdateActiveStatusAsync类似,直接更新IsActive字段
- GetDataRuleStatsAsync: 创建DataRuleStats对象,包含所有统计字段
- GetApplicableRulesAsync: 根据deviceId、dataType和appCode筛选规则

### DictionaryRepository

- GetByTypeCodeAsync: 使用Type字段和Status筛选
- GetByTypeAndCodeAsync: 组合Type和Code字段查询
- GetByStatusAsync: 根据Status字段筛选
- TypeCodeExistsAsync: 查询DictionaryTypeConfigs表
- ItemCodeExistsAsync: 查询DictionaryItems表
- GetActiveItemsByTypeAsync: 筛选Status=active的项
- UpdateItemStatusAsync: 更新DictionaryItem.Status
- UpdateTypeStatusAsync: 更新DictionaryTypeConfig.IsActive
- GetDictionaryStatsAsync: 创建DictionaryStats对象,包含所有统计字段

### ETLTaskRepository

- GetByTaskTypeAsync: 根据TaskType筛选
- GetByStatusAsync: 修改签名,添加appCode参数,返回IEnumerable而非List
- GetTasksToRunAsync: 筛选Status=running/ready的任务
- UpdateTaskStatusAsync: 修改签名,添加lastRunTime和nextRunTime参数
- NameExistsAsync: 检查Name字段
- GetExecutionHistoryAsync: 创建TaskExecutionHistory对象列表
- RecordExecutionHistoryAsync: 创建并保存TaskExecutionHistory记录
- GetETLTaskStatsAsync: 创建ETLTaskStats对象,包含所有统计字段

### SystemSettingRepository

- GetByKeyAsync: 统一处理appCode可选参数的两个重载
- GetByCategoryAsync: 根据Category字段筛选
- KeyExistsAsync: 检查SettingKey是否存在
- GetOrCreateAsync: 先查询,不存在则创建并返回
- GetValueAsync<T>: 通用泛型方法,支持类型转换
- SetValueAsync<T>: 通用泛型方法,支持值转换
- GetValuesAsync: 根据keys列表批量查询
- SetValuesAsync: 批量更新设置值
- GetCategoriesAsync: 查询并返回所有不重复的Category

## 目录结构

```
g:\IoTPlatform\Data\Repositories\Implementations\
├── DataRuleRepository.cs         # [MODIFY] 添加3个方法实现和修正1个返回类型
├── DictionaryRepository.cs       # [MODIFY] 添加9个方法实现和修正1个返回类型
├── ETLTaskRepository.cs          # [MODIFY] 添加8个方法实现,修正2个方法签名
└── SystemSettingRepository.cs    # [MODIFY] 添加8个方法实现,统一2个重载方法
```

## 关键代码结构

### DataRuleStats类(接口已定义)

```
public class DataRuleStats
{
    public int TotalRules { get; set; }
    public int ActiveRules { get; set; }
    public int InactiveRules { get; set; }
    public int AlertRules { get; set; }
    public int TransformRules { get; set; }
    public int ValidationRules { get; set; }
    public int CriticalRules { get; set; }
    public int WarningRules { get; set; }
    public int InfoRules { get; set; }
    public DateTime LastUpdate { get; set; } = DateTime.UtcNow;
}
```

### DictionaryStats类(接口已定义)

```
public class DictionaryStats
{
    public int TotalTypes { get; set; }
    public int ActiveTypes { get; set; }
    public int TotalItems { get; set; }
    public int ActiveItems { get; set; }
    public int InactiveItems { get; set; }
    public DateTime LastUpdate { get; set; } = DateTime.UtcNow;
}
```

### ETLTaskStats类(接口已定义)

```
public class ETLTaskStats
{
    public int TotalTasks { get; set; }
    public int ActiveTasks { get; set; }
    public int PausedTasks { get; set; }
    public int FailedTasks { get; set; }
    public int CompletedTasks { get; set; }
    public int TransformTasks { get; set; }
    public int AggregationTasks { get; set; }
    public int ExportTasks { get; set; }
    public int ScheduledTasks { get; set; }
    public int ManualTasks { get; set; }
    public DateTime LastUpdate { get; set; } = DateTime.UtcNow;
}
```

### TaskExecutionHistory类(接口已定义)

```
public class TaskExecutionHistory
{
    public long Id { get; set; }
    public long TaskId { get; set; }
    public DateTime ExecutionTime { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? Message { get; set; }
    public int? Duration { get; set; }
}
```

此任务不涉及UI设计,纯粹的后端代码实现。