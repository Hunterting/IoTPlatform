using IoTPlatform.Data;
using IoTPlatform.DTOs.Responses;
using IoTPlatform.Models;
using Microsoft.EntityFrameworkCore;

namespace IoTPlatform.Services;

/// <summary>
/// 日志服务实现
/// </summary>
public class LogService : ILogService
{
    private readonly AppDbContext _dbContext;

    public LogService(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>
    /// 获取操作日志列表
    /// </summary>
    public async Task<PagedResponse<OperationLogDto>> GetOperationLogsAsync(int page, int pageSize, string? module, string? action, long? userId, DateTime? startTime, DateTime? endTime, string? appCode, string? currentUserRole)
    {
        var query = _dbContext.OperationLogs.AsQueryable();

        // 超级管理员可以查看所有租户日志，其他角色只能查看所属租户日志
        if (currentUserRole != Configuration.Roles.SUPER_ADMIN && !string.IsNullOrEmpty(appCode))
        {
            query = query.Where(l => l.AppCode == appCode);
        }

        // 模块筛选
        if (!string.IsNullOrEmpty(module))
        {
            query = query.Where(l => l.Module == module);
        }

        // 操作类型筛选
        if (!string.IsNullOrEmpty(action))
        {
            query = query.Where(l => l.Action == action);
        }

        // 用户筛选
        if (userId.HasValue)
        {
            query = query.Where(l => l.UserId == userId.Value);
        }

        // 时间范围筛选
        if (startTime.HasValue)
        {
            query = query.Where(l => l.Time >= startTime.Value);
        }

        if (endTime.HasValue)
        {
            query = query.Where(l => l.Time <= endTime.Value);
        }

        var totalCount = await query.CountAsync();

        var logs = await query
            .OrderByDescending(l => l.Time)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(l => new OperationLogDto
            {
                Id = l.Id,
                UserId = l.UserId,
                UserName = l.UserName,
                Role = l.Role,
                Time = l.Time,
                Module = l.Module,
                Action = l.Action,
                Target = l.Target,
                Detail = l.Detail,
                IP = l.IP,
                Status = l.Status,
                Duration = l.Duration,
                AppCode = l.AppCode
            })
            .ToListAsync();

        return PagedResponse<OperationLogDto>.Create(logs, totalCount, page, pageSize);
    }

    /// <summary>
    /// 获取登录日志列表
    /// </summary>
    public async Task<PagedResponse<LoginLogDto>> GetLoginLogsAsync(int page, int pageSize, long? userId, string? status, DateTime? startTime, DateTime? endTime, string? appCode, string? currentUserRole)
    {
        var query = _dbContext.LoginLogs.AsQueryable();

        // 超级管理员可以查看所有租户日志，其他角色只能查看所属租户日志
        if (currentUserRole != Configuration.Roles.SUPER_ADMIN && !string.IsNullOrEmpty(appCode))
        {
            query = query.Where(l => l.AppCode == appCode);
        }

        // 用户筛选
        if (userId.HasValue)
        {
            query = query.Where(l => l.UserId == userId.Value);
        }

        // 状态筛选
        if (!string.IsNullOrEmpty(status))
        {
            query = query.Where(l => l.Status == status);
        }

        // 时间范围筛选
        if (startTime.HasValue)
        {
            query = query.Where(l => l.LoginTime >= startTime.Value);
        }

        if (endTime.HasValue)
        {
            query = query.Where(l => l.LoginTime <= endTime.Value);
        }

        var totalCount = await query.CountAsync();

        var logs = await query
            .OrderByDescending(l => l.LoginTime)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(l => new LoginLogDto
            {
                Id = l.Id,
                UserId = l.UserId,
                UserName = l.UserName,
                Role = l.Role,
                LoginTime = l.LoginTime,
                IP = l.IP,
                UserAgent = l.UserAgent,
                Status = l.Status,
                FailReason = l.FailReason,
                AppCode = l.AppCode
            })
            .ToListAsync();

        return PagedResponse<LoginLogDto>.Create(logs, totalCount, page, pageSize);
    }

    /// <summary>
    /// 获取操作日志详情
    /// </summary>
    public async Task<OperationLogDto?> GetOperationLogAsync(long id, string? appCode, string? currentUserRole)
    {
        var query = _dbContext.OperationLogs.AsQueryable();

        // 超级管理员可以查看所有租户日志，其他角色只能查看所属租户日志
        if (currentUserRole != Configuration.Roles.SUPER_ADMIN && !string.IsNullOrEmpty(appCode))
        {
            query = query.Where(l => l.AppCode == appCode);
        }

        var log = await query.FirstOrDefaultAsync(l => l.Id == id);
        if (log == null) return null;

        return new OperationLogDto
        {
            Id = log.Id,
            UserId = log.UserId,
            UserName = log.UserName,
            Role = log.Role,
            Time = log.Time,
            Module = log.Module,
            Action = log.Action,
            Target = log.Target,
            Detail = log.Detail,
            IP = log.IP,
            Status = log.Status,
            Duration = log.Duration,
            AppCode = log.AppCode
        };
    }

    /// <summary>
    /// 获取登录日志详情
    /// </summary>
    public async Task<LoginLogDto?> GetLoginLogAsync(long id, string? appCode, string? currentUserRole)
    {
        var query = _dbContext.LoginLogs.AsQueryable();

        // 超级管理员可以查看所有租户日志，其他角色只能查看所属租户日志
        if (currentUserRole != Configuration.Roles.SUPER_ADMIN && !string.IsNullOrEmpty(appCode))
        {
            query = query.Where(l => l.AppCode == appCode);
        }

        var log = await query.FirstOrDefaultAsync(l => l.Id == id);
        if (log == null) return null;

        return new LoginLogDto
        {
            Id = log.Id,
            UserId = log.UserId,
            UserName = log.UserName,
            Role = log.Role,
            LoginTime = log.LoginTime,
            IP = log.IP,
            UserAgent = log.UserAgent,
            Status = log.Status,
            FailReason = log.FailReason,
            AppCode = log.AppCode
        };
    }
}
