using IoTPlatform.Data;
using IoTPlatform.DTOs.Responses;
using IoTPlatform.Models;
using Microsoft.EntityFrameworkCore;

namespace IoTPlatform.Services;

/// <summary>
/// 设备授权服务实现。
///
/// 【关键业务边界】默认所有设备无授权记录时派生为 pending（待授权），**不禁用**。
/// 只有「显式授权过且已过期」的记录才判定为禁用。这样新表上线不会打断存量设备
/// 采集与真机联调（本会话设备 Id=7 正在上报），也不会破坏既有 944 个测试。
/// </summary>
public class DeviceAuthorizationService : IDeviceAuthorizationService
{
    public const string AlertTypeExpiring = "authorization_expiring";
    public const string AlertTypeExpired = "authorization_expired";

    private readonly AppDbContext _db;
    private readonly ILogger<DeviceAuthorizationService> _logger;

    public DeviceAuthorizationService(AppDbContext db, ILogger<DeviceAuthorizationService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public int ExpiringSoonDays => 30;

    // ── 列表 ──────────────────────────────────────────────────────────────────

    public async Task<List<DeviceAuthorizationDto>> GetListAsync(string? appCode, CancellationToken ct = default)
    {
        // 真实设备列表（租户级由全局查询过滤器处理；appCode 非空时再显式收窄）
        var deviceQuery = _db.Devices.AsNoTracking().Include(d => d.Area).AsQueryable();
        if (!string.IsNullOrEmpty(appCode))
            deviceQuery = deviceQuery.Where(d => d.AppCode == appCode);
        var devices = await deviceQuery.OrderBy(d => d.Id).ToListAsync(ct);

        // 授权记录（跨租户场景用 IgnoreQueryFilters，避免 HostedService 无上下文时漏数据）
        var authzQuery = _db.DeviceAuthorizations.AsNoTracking().IgnoreQueryFilters().AsQueryable();
        if (!string.IsNullOrEmpty(appCode))
            authzQuery = authzQuery.Where(a => a.AppCode == appCode);
        var authzList = await authzQuery.ToListAsync(ct);
        var byDevice = new Dictionary<long, DeviceAuthorization>();
        foreach (var a in authzList)
        {
            // 同一设备理论上唯一（唯一索引），取最新一条兜底
            if (!byDevice.ContainsKey(a.DeviceId) || a.Id > byDevice[a.DeviceId].Id)
                byDevice[a.DeviceId] = a;
        }

        var now = DateTime.UtcNow;
        var threshold = now.AddDays(ExpiringSoonDays);
        var result = new List<DeviceAuthorizationDto>(devices.Count);

        foreach (var d in devices)
        {
            var dto = new DeviceAuthorizationDto
            {
                DeviceId = d.Id,
                DeviceName = d.Name,
                Model = d.Model,
                SerialNumber = d.SerialNumber,
                Category = d.Category,
                AreaId = d.AreaId,
                AreaName = d.Area?.Name,
                AppCode = d.AppCode,
                DeviceStatus = d.Status,
            };

            if (byDevice.TryGetValue(d.Id, out var a))
            {
                dto.AuthorizationId = a.Id;
                dto.AuthorizedAt = a.AuthorizedAt;
                dto.ExpireAt = a.ExpireAt;
                dto.AuthorizedByName = a.AuthorizedByName;
                dto.Remark = a.Remark;
                dto.DaysUntilExpire = (int)Math.Ceiling((a.ExpireAt - now).TotalDays);

                if (a.Status == "revoked")
                {
                    dto.AuthorizationStatus = "revoked";
                    dto.StatusLabel = "已撤销";
                }
                else if (a.ExpireAt <= now)
                {
                    dto.AuthorizationStatus = "expired";
                    dto.StatusLabel = "已过期";
                    dto.IsDisabled = true; // 已授权且过期 → 禁用
                }
                else if (a.ExpireAt <= threshold)
                {
                    dto.AuthorizationStatus = "expiring_soon";
                    dto.StatusLabel = $"即将过期（{dto.DaysUntilExpire} 天）";
                    dto.IsExpiringSoon = true;
                }
                else
                {
                    dto.AuthorizationStatus = "active";
                    dto.StatusLabel = "已授权";
                }
            }
            else
            {
                // 无授权记录 = 待授权，不禁用
                dto.AuthorizationStatus = "pending";
                dto.StatusLabel = "待授权";
            }

            result.Add(dto);
        }

        return result;
    }

    // ── 统计 ──────────────────────────────────────────────────────────────────

    public async Task<DeviceAuthorizationSummaryDto> GetSummaryAsync(string? appCode, CancellationToken ct = default)
    {
        var list = await GetListAsync(appCode, ct);
        return new DeviceAuthorizationSummaryDto
        {
            Total = list.Count,
            Active = list.Count(x => x.AuthorizationStatus == "active"),
            ExpiringSoon = list.Count(x => x.AuthorizationStatus == "expiring_soon"),
            Expired = list.Count(x => x.AuthorizationStatus == "expired"),
            Pending = list.Count(x => x.AuthorizationStatus == "pending"),
            Revoked = list.Count(x => x.AuthorizationStatus == "revoked"),
            ExpiringSoonDays = ExpiringSoonDays,
        };
    }

    // ── 授权 / 续期 ──────────────────────────────────────────────────────────

    public async Task<DeviceAuthorizationDto> GrantAsync(
        GrantDeviceAuthorizationRequest request, long? operatorId, string? operatorName,
        string? callerAppCode, CancellationToken ct = default)
    {
        // 目标租户：超管可显式指定（代租户授权），否则用设备自身 AppCode
        var device = await _db.Devices.AsNoTracking().IgnoreQueryFilters()
            .FirstOrDefaultAsync(d => d.Id == request.DeviceId, ct)
            ?? throw new InvalidOperationException("设备不存在");

        var effectiveAppCode = !string.IsNullOrWhiteSpace(request.AppCode)
            ? request.AppCode
            : (!string.IsNullOrWhiteSpace(callerAppCode) ? callerAppCode : device.AppCode);

        var expireAt = request.ExpireAt ?? DateTime.UtcNow.AddDays(Math.Max(1, request.ExpireDays));

        var existing = await _db.DeviceAuthorizations.IgnoreQueryFilters()
            .FirstOrDefaultAsync(a => a.DeviceId == request.DeviceId && a.AppCode == effectiveAppCode, ct);

        if (existing == null)
        {
            existing = new DeviceAuthorization
            {
                DeviceId = request.DeviceId,
                AppCode = effectiveAppCode,
                AuthorizedAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow,
            };
            _db.DeviceAuthorizations.Add(existing);
        }

        existing.Status = "active";
        existing.ExpireAt = expireAt;
        existing.AuthorizedBy = operatorId;
        existing.AuthorizedByName = operatorName;
        existing.Remark = request.Remark;
        existing.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        var list = await GetListAsync(null, ct);
        return list.First(x => x.DeviceId == request.DeviceId);
    }

    // ── 撤销 ──────────────────────────────────────────────────────────────────

    public async Task<DeviceAuthorizationDto> RevokeAsync(
        RevokeDeviceAuthorizationRequest request, string? callerAppCode, CancellationToken ct = default)
    {
        var device = await _db.Devices.AsNoTracking().IgnoreQueryFilters()
            .FirstOrDefaultAsync(d => d.Id == request.DeviceId, ct)
            ?? throw new InvalidOperationException("设备不存在");

        var effectiveAppCode = !string.IsNullOrWhiteSpace(request.AppCode)
            ? request.AppCode
            : (!string.IsNullOrWhiteSpace(callerAppCode) ? callerAppCode : device.AppCode);

        var existing = await _db.DeviceAuthorizations.IgnoreQueryFilters()
            .FirstOrDefaultAsync(a => a.DeviceId == request.DeviceId && a.AppCode == effectiveAppCode, ct)
            ?? throw new InvalidOperationException("该设备尚无授权记录");

        existing.Status = "revoked";
        existing.Remark = request.Remark ?? existing.Remark;
        existing.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        var list = await GetListAsync(null, ct);
        return list.First(x => x.DeviceId == request.DeviceId);
    }

    // ── 过期禁用判定 ──────────────────────────────────────────────────────────

    public async Task<bool> IsDeviceDisabledAsync(long deviceId, CancellationToken ct = default)
    {
        // 只有「已授权(status != revoked) 且 ExpireAt 已过」才禁用；
        // 无记录(pending) / revoked / 未到期 一律不禁用。
        return await _db.DeviceAuthorizations
            .AsNoTracking()
            .IgnoreQueryFilters()
            .AnyAsync(a => a.DeviceId == deviceId
                && a.Status != "revoked"
                && a.ExpireAt <= DateTime.UtcNow, ct);
    }

    // ── 定时扫描：生成去重告警，按租户隔离 ─────────────────────────────────────

    public async Task<int> ScanAndNotifyExpiringAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var threshold = now.AddDays(ExpiringSoonDays);
        var today = now.Date;

        // 候选：active 且即将过期/已过期的授权（跨租户）
        var candidates = await _db.DeviceAuthorizations
            .AsNoTracking().IgnoreQueryFilters()
            .Where(a => a.Status == "active" && a.ExpireAt <= threshold)
            .ToListAsync(ct);

        if (candidates.Count == 0) return 0;

        var deviceIds = candidates.Select(a => a.DeviceId).Distinct().ToList();
        var devices = await _db.Devices.AsNoTracking().IgnoreQueryFilters()
            .Include(d => d.Area)
            .Where(d => deviceIds.Contains(d.Id))
            .ToListAsync(ct);
        var deviceMap = devices.ToDictionary(d => d.Id);

        // 今日已生成的同类告警（去重：DeviceId + AlertType + 当天）
        var existingAlerts = await _db.AlertRecords.AsNoTracking().IgnoreQueryFilters()
            .Where(x => deviceIds.Contains(x.DeviceId ?? 0)
                && (x.AlertType == AlertTypeExpiring || x.AlertType == AlertTypeExpired)
                && x.CreatedAt >= today)
            .Select(x => new { x.DeviceId, x.AlertType })
            .ToListAsync(ct);
        var existingKeys = new HashSet<string>(
            existingAlerts.Select(x => $"{x.DeviceId}:{x.AlertType}"), StringComparer.OrdinalIgnoreCase);

        int created = 0;
        foreach (var a in candidates)
        {
            if (!deviceMap.TryGetValue(a.DeviceId, out var d)) continue;
            var isExpired = a.ExpireAt <= now;
            var alertType = isExpired ? AlertTypeExpired : AlertTypeExpiring;
            var key = $"{a.DeviceId}:{alertType}";
            if (existingKeys.Contains(key)) continue; // 每天每设备每类型最多一条

            // 已过期设备：把该设备遗留的「即将过期」未处理告警标记为已解决，
            // 避免租户同时看到「即将过期」与「已过期」两条互相矛盾的信息。
            if (isExpired)
            {
                var stale = await _db.AlertRecords.IgnoreQueryFilters()
                    .Where(x => x.DeviceId == a.DeviceId
                        && x.AlertType == AlertTypeExpiring
                        && x.Status == "pending")
                    .ToListAsync(ct);
                foreach (var s in stale)
                {
                    s.Status = "resolved";
                    s.ResolveTime = now;
                    s.UpdatedAt = now;
                }
            }

            var days = (int)Math.Ceiling((a.ExpireAt - now).TotalDays);
            var remark = isExpired
                ? $"设备「{d.Name}」授权已于 {a.ExpireAt:yyyy-MM-dd} 过期，采集与指令已被禁用，请联系管理员续期。"
                : $"设备「{d.Name}」授权将于 {a.ExpireAt:yyyy-MM-dd} 到期（剩余 {days} 天），请及时续期。";

            _db.AlertRecords.Add(new AlertRecord
            {
                AlertNo = $"ALT-AUTHZ-{now:yyyyMMdd}-{a.DeviceId}-{(isExpired ? "EXP" : "SOON")}",
                DeviceName = d.Name,
                DeviceCode = d.SerialNumber,
                AreaName = d.Area?.Name,
                AlertType = alertType,
                Level = isExpired ? "critical" : "warning",
                AlertTime = now,
                Status = "pending",
                Remark = remark,
                DeviceId = d.Id,
                AreaId = d.AreaId,
                AppCode = a.AppCode ?? d.AppCode, // 显式租户隔离，租户在告警中心可见
                CreatedAt = now,
                UpdatedAt = now,
            });
            existingKeys.Add(key);
            created++;
        }

        if (created > 0)
        {
            await _db.SaveChangesAsync(ct);
            _logger.LogInformation("设备授权到期扫描：生成 {Count} 条提醒告警", created);
        }
        return created;
    }
}
