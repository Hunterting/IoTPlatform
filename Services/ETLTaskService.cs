using IoTPlatform.Data.Repositories.Interfaces;
using IoTPlatform.DTOs.Requests;
using IoTPlatform.DTOs.Responses;
using IoTPlatform.Helpers;
using IoTPlatform.Models;
using IoTPlatform.Services.Guards;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace IoTPlatform.Services;

/// <summary>
/// ETL任务服务实现（使用仓储模式）
/// </summary>
public class ETLTaskService : IETLTaskService
{
    private readonly IETLTaskRepository _etlTaskRepository;
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
        RequireGuard().AssertWriteScopeByAppCodeAsync(scope, appCode, IsSystemRow(appCode), true, ct);

    public ETLTaskService(
        IETLTaskRepository etlTaskRepository,
        IUnitOfWork unitOfWork,
        ICustomerScopeGuard? scopeGuard = null)
    {
        _etlTaskRepository = etlTaskRepository;
        _unitOfWork = unitOfWork;
        _scopeGuard = scopeGuard;
    }

    /// <summary>
    /// 获取ETL任务列表
    /// </summary>
        /// <summary>旧签名保留：委托到范围下推重载，下级/内部路径零回归。</summary>
    public Task<PagedResponse<ETLTaskDto>> GetETLTasksAsync(int page, int pageSize, string? keyword, string? taskType, string? appCode) => GetETLTasksAsync(page, pageSize, keyword, taskType, appCode, null, null);

    /// <summary>B4/M5：admin 的 appCode=null 不再等于零过滤。</summary>
    public async Task<PagedResponse<ETLTaskDto>> GetETLTasksAsync(int page, int pageSize, string? keyword, string? taskType, string? appCode, IReadOnlyCollection<long>? customerIds, IReadOnlyCollection<string>? appCodes = null)
    {
        var query = _etlTaskRepository.GetQueryable();
        query = query.ApplyQueryScope(appCode, customerIds, appCodes);

        // 租户数据隔离
        if (!string.IsNullOrEmpty(appCode))
        {
            query = query.Where(t => t.AppCode == appCode);
        }

        // 任务类型筛选
        if (!string.IsNullOrEmpty(taskType))
        {
            query = query.Where(t => t.TaskType == taskType);
        }

        // 关键词搜索
        if (!string.IsNullOrEmpty(keyword))
        {
            query = query.Where(t =>
                t.Name.Contains(keyword) ||
                (t.Description != null && t.Description.Contains(keyword)));
        }

        var totalCount = await query.CountAsync();

        var tasks = await query
            .OrderByDescending(t => t.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(t => new ETLTaskDto
            {
                Id = t.Id,
                Name = t.Name,
                TaskType = t.TaskType,
                SourceConfig = t.SourceConfig,
                TargetConfig = t.TargetConfig,
                TransformRule = t.TransformRule,
                Schedule = t.Schedule,
                Status = t.Status,
                LastRunTime = t.LastRunTime,
                NextRunTime = t.NextRunTime,
                AppCode = t.AppCode,
                Description = t.Description,
                CreatedAt = t.CreatedAt,
                UpdatedAt = t.UpdatedAt
            })
            .ToListAsync();

        return PagedResponse<ETLTaskDto>.Create(tasks, totalCount, page, pageSize);
    }

    /// <summary>
    /// 获取ETL任务详情
    /// </summary>
    public async Task<ETLTaskDto?> GetETLTaskAsync(long id, string? appCode)
    {
        var query = _etlTaskRepository.GetQueryable();

        // 租户数据隔离
        if (!string.IsNullOrEmpty(appCode))
        {
            query = query.Where(t => t.AppCode == appCode);
        }

        var task = await query.FirstOrDefaultAsync(t => t.Id == id);
        if (task == null) return null;

        return new ETLTaskDto
        {
            Id = task.Id,
            Name = task.Name,
            TaskType = task.TaskType,
            SourceConfig = task.SourceConfig,
            TargetConfig = task.TargetConfig,
            TransformRule = task.TransformRule,
            Schedule = task.Schedule,
            Status = task.Status,
            LastRunTime = task.LastRunTime,
            NextRunTime = task.NextRunTime,
            AppCode = task.AppCode,
            Description = task.Description,
            CreatedAt = task.CreatedAt,
            UpdatedAt = task.UpdatedAt
        };
    }

    /// <summary>
    /// 创建ETL任务
    /// </summary>
    public async Task<ETLTaskDto> CreateETLTaskAsync(CreateETLTaskRequest request, CustomerScope? scope = null)
    {
        var task = new ETLTask
        {
            Name = request.Name,
            TaskType = request.TaskType,
            SourceConfig = request.SourceConfig,
            TargetConfig = request.TargetConfig,
            TransformRule = request.TransformRule,
            Schedule = request.Schedule,
            Status = "paused",
            AppCode = request.AppCode,
            Description = request.Description,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        if (scope != null)
        {
            await AssertWriteAsync(scope, task.AppCode);
        }

        await _etlTaskRepository.AddAsync(task);
        await _unitOfWork.SaveChangesAsync();

        return new ETLTaskDto
        {
            Id = task.Id,
            Name = task.Name,
            TaskType = task.TaskType,
            SourceConfig = task.SourceConfig,
            TargetConfig = task.TargetConfig,
            TransformRule = task.TransformRule,
            Schedule = task.Schedule,
            Status = task.Status,
            LastRunTime = task.LastRunTime,
            NextRunTime = task.NextRunTime,
            AppCode = task.AppCode,
            Description = task.Description,
            CreatedAt = task.CreatedAt,
            UpdatedAt = task.UpdatedAt
        };
    }

    /// <summary>
    /// 更新ETL任务
    /// </summary>
    public async Task<ETLTaskDto> UpdateETLTaskAsync(long id, UpdateETLTaskRequest request, string? appCode, CustomerScope? scope = null)
    {
        var task = await _etlTaskRepository.GetByIdAsync(id);
        if (task == null)
        {
            throw new InvalidOperationException("ETL任务不存在");
        }

        // 权限检查：新路径按 scope fail-closed；旧签名仅供内部通道沿用。
        if (scope != null)
        {
            await AssertWriteAsync(scope, task.AppCode);
        }
        else if (!string.IsNullOrEmpty(appCode) && task.AppCode != appCode)
        {
            throw new UnauthorizedAccessException("无权修改该ETL任务");
        }

        task.Name = request.Name;
        task.SourceConfig = request.SourceConfig ?? task.SourceConfig;
        task.TargetConfig = request.TargetConfig ?? task.TargetConfig;
        task.TransformRule = request.TransformRule ?? task.TransformRule;
        task.Schedule = request.Schedule ?? task.Schedule;
        task.Status = request.Status ?? task.Status;
        task.Description = request.Description ?? task.Description;
        task.UpdatedAt = DateTime.UtcNow;

        await _etlTaskRepository.UpdateAsync(task);
        await _unitOfWork.SaveChangesAsync();

        return new ETLTaskDto
        {
            Id = task.Id,
            Name = task.Name,
            TaskType = task.TaskType,
            SourceConfig = task.SourceConfig,
            TargetConfig = task.TargetConfig,
            TransformRule = task.TransformRule,
            Schedule = task.Schedule,
            Status = task.Status,
            LastRunTime = task.LastRunTime,
            NextRunTime = task.NextRunTime,
            AppCode = task.AppCode,
            Description = task.Description,
            CreatedAt = task.CreatedAt,
            UpdatedAt = task.UpdatedAt
        };
    }

    /// <summary>
    /// 删除ETL任务
    /// </summary>
    public async Task DeleteETLTaskAsync(long id, string? appCode, CustomerScope? scope = null)
    {
        var task = await _etlTaskRepository.GetByIdAsync(id);
        if (task == null)
        {
            throw new InvalidOperationException("ETL任务不存在");
        }

        // 权限检查：新路径按 scope fail-closed；旧签名仅供内部通道沿用。
        if (scope != null)
        {
            await AssertWriteAsync(scope, task.AppCode);
        }
        else if (!string.IsNullOrEmpty(appCode) && task.AppCode != appCode)
        {
            throw new UnauthorizedAccessException("无权删除该ETL任务");
        }

        // 检查任务是否正在运行
        if (task.Status == "active")
        {
            throw new InvalidOperationException("任务正在运行，无法删除。请先停止任务。");
        }

        await _etlTaskRepository.DeleteAsync(task);
        await _unitOfWork.SaveChangesAsync();
    }

    /// <summary>
    /// 启动ETL任务
    /// </summary>
    public async Task StartETLTaskAsync(long id, string? appCode, CustomerScope? scope = null)
    {
        var task = await _etlTaskRepository.GetByIdAsync(id);
        if (task == null)
        {
            throw new InvalidOperationException("ETL任务不存在");
        }

        // 权限检查：副作用（写状态/调度）之前 fail-closed。
        if (scope != null)
        {
            await AssertWriteAsync(scope, task.AppCode);
        }
        else if (!string.IsNullOrEmpty(appCode) && task.AppCode != appCode)
        {
            throw new UnauthorizedAccessException("无权启动该ETL任务");
        }

        // TODO: 计算下次运行时间（解析cron表达式）
        task.Status = "active";
        task.NextRunTime = DateTime.UtcNow.AddMinutes(1); // 临时设置
        task.UpdatedAt = DateTime.UtcNow;
        await _etlTaskRepository.UpdateAsync(task);
        await _unitOfWork.SaveChangesAsync();

        // TODO: 这里应该将任务添加到调度队列
    }

    /// <summary>
    /// 停止ETL任务
    /// </summary>
    public async Task StopETLTaskAsync(long id, string? appCode, CustomerScope? scope = null)
    {
        var task = await _etlTaskRepository.GetByIdAsync(id);
        if (task == null)
        {
            throw new InvalidOperationException("ETL任务不存在");
        }

        // 权限检查：副作用（写状态）之前 fail-closed。
        if (scope != null)
        {
            await AssertWriteAsync(scope, task.AppCode);
        }
        else if (!string.IsNullOrEmpty(appCode) && task.AppCode != appCode)
        {
            throw new UnauthorizedAccessException("无权停止该ETL任务");
        }

        task.Status = "paused";
        task.UpdatedAt = DateTime.UtcNow;
        await _etlTaskRepository.UpdateAsync(task);
        await _unitOfWork.SaveChangesAsync();
    }

    /// <summary>
    /// 执行ETL任务
    /// </summary>
    public async Task ExecuteETLTaskAsync(ETLTask task)
    {
        // TODO: 实现ETL任务执行逻辑
        // 1. 解析SourceConfig
        // 2. 解析TargetConfig
        // 3. 执行转换规则
        // 4. 处理数据

        task.LastRunTime = DateTime.UtcNow;
        task.Status = "completed";
        task.UpdatedAt = DateTime.UtcNow;
        await _etlTaskRepository.UpdateAsync(task);
        await _unitOfWork.SaveChangesAsync();
    }
}
