using System.Security.Claims;
using IoTPlatform.Configuration;
using IoTPlatform.Data;
using IoTPlatform.Data.Repositories.Interfaces;
using IoTPlatform.Helpers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace IoTPlatform.Controllers;

/// <summary>
/// 租户管理员（admin）授权客户集合分配接口（多客户授权模型 M01/M02，T04.1）。
///
/// <para><b>唯一写入口</b>：仅超管可调用；全量替换 user_customers。建户接口绝不写本表（设计 §7.2-9）。</para>
/// <para><b>即时生效</b>：本接口只改 DB，不刷新任何 JWT claim；ICustomerScopeService 每次请求从 DB 读取，
/// 因此 admin 持旧 token 的下一次请求即可看到新的授权集合（"claim 是快照、DB 是权威"）。</para>
/// <para><b>响应约定</b>：HTTP 恒 200，业务成败看 ApiResponse.Code（设计 §7.2-6）。</para>
/// </summary>
[ApiController]
[Route("api/v1/users")]
[Authorize]
public class AdminCustomerGrantsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IUserCustomerRepository _grants;

    public AdminCustomerGrantsController(AppDbContext db, IUserCustomerRepository grants)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _grants = grants ?? throw new ArgumentNullException(nameof(grants));
    }

    /// <summary>
    /// 全量替换指定 admin 的授权客户集合。
    /// PUT /api/v1/users/{userId}/managed-customers，body: {"customerIds":[1,2,4]}。
    /// </summary>
    [HttpPut("{userId}/managed-customers")]
    public async Task<ActionResult<ApiResponse<ManagedCustomerGrantDto>>> Assign(
        long userId,
        [FromBody] AssignManagedCustomersRequest? request)
    {
        // ── 铁律：仅超管。业务 403（HTTP 200），不使用 HTTP 403 状态码 ──
        if (!IsSuperAdmin())
        {
            return Ok(ApiResponse<ManagedCustomerGrantDto>.Fail(403, "仅超级管理员可分配租户管理员的授权客户。"));
        }

        var customerIds = request?.CustomerIds;

        // 上限与空集合校验：超限/空集统一 Code=400（设计 §8 清单 7）。
        var max = CustomerScopeConstants.DefaultMaxManagedCustomers;
        if (customerIds == null || customerIds.Length == 0)
        {
            return Ok(ApiResponse<ManagedCustomerGrantDto>.BadRequest("授权客户集合不能为空。"));
        }

        var distinctIds = customerIds.Where(id => id > 0).Distinct().ToArray();
        if (distinctIds.Length != customerIds.Length)
        {
            return Ok(ApiResponse<ManagedCustomerGrantDto>.BadRequest("客户 ID 必须为正整数且不得重复。"));
        }

        if (distinctIds.Length > max)
        {
            return Ok(ApiResponse<ManagedCustomerGrantDto>.BadRequest(
                $"单个租户管理员最多可管理 {max} 个客户，当前 {distinctIds.Length} 个。"));
        }

        try
        {
            // 目标用户必须存在且 role == admin（customer_admin / 下级 / 超管均不可作为分配目标）。
            var targetRole = await _db.Users
                .AsNoTracking()
                .IgnoreQueryFilters()
                .Where(u => u.Id == userId)
                .Select(u => u.Role)
                .FirstOrDefaultAsync();

            if (targetRole == null)
            {
                return Ok(ApiResponse<ManagedCustomerGrantDto>.NotFound("用户不存在"));
            }

            if (targetRole != Roles.ADMIN)
            {
                return Ok(ApiResponse<ManagedCustomerGrantDto>.BadRequest("只能给租户管理员（admin）分配客户。"));
            }

            // 客户必须全部真实存在，避免留下无法反查的悬空授权。
            var existingCount = await _db.Customers
                .AsNoTracking()
                .IgnoreQueryFilters()
                .CountAsync(c => distinctIds.Contains(c.Id));

            if (existingCount != distinctIds.Length)
            {
                return Ok(ApiResponse<ManagedCustomerGrantDto>.BadRequest("授权集合中存在不存在的客户。"));
            }

            await _grants.SyncAsync(userId, distinctIds, GetCurrentUserId());

            var dto = await BuildGrantDtoAsync(userId, distinctIds);
            return Ok(ApiResponse<ManagedCustomerGrantDto>.Success(dto, "授权客户已保存"));
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse<ManagedCustomerGrantDto>.Error(ex.Message));
        }
    }

    /// <summary>
    /// 回显指定 admin 当前授权客户集合。
    /// GET /api/v1/users/{userId}/managed-customers。
    /// </summary>
    [HttpGet("{userId}/managed-customers")]
    public async Task<ActionResult<ApiResponse<ManagedCustomerGrantDto>>> Get(long userId)
    {
        if (!IsSuperAdmin())
        {
            return Ok(ApiResponse<ManagedCustomerGrantDto>.Fail(403, "仅超级管理员可查看租户管理员的授权客户。"));
        }

        try
        {
            var targetRole = await _db.Users
                .AsNoTracking()
                .IgnoreQueryFilters()
                .Where(u => u.Id == userId)
                .Select(u => u.Role)
                .FirstOrDefaultAsync();

            if (targetRole == null)
            {
                return Ok(ApiResponse<ManagedCustomerGrantDto>.NotFound("用户不存在"));
            }

            if (targetRole != Roles.ADMIN)
            {
                return Ok(ApiResponse<ManagedCustomerGrantDto>.BadRequest("只能查看租户管理员（admin）的授权客户。"));
            }

            var ids = await _grants.GetCustomerIdsAsync(userId);
            var dto = await BuildGrantDtoAsync(userId, ids);
            return Ok(ApiResponse<ManagedCustomerGrantDto>.Success(dto));
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse<ManagedCustomerGrantDto>.Error(ex.Message));
        }
    }

    /// <summary>统一超管判据（设计 §7.2-2）。</summary>
    private bool IsSuperAdmin() => Roles.IsSuperAdmin(User.FindFirst(ClaimTypes.Role)?.Value);

    /// <summary>当前超管 ID，写入 user_customers.GrantedByUserId 供 M10 审计使用。</summary>
    private long? GetCurrentUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return long.TryParse(claim, out var id) ? id : null;
    }

    /// <summary>组装回显 DTO；UpdatedAt 取当前授权行的最大更新时间，无授权时为 MinValue。</summary>
    private async Task<ManagedCustomerGrantDto> BuildGrantDtoAsync(
        long userId,
        IReadOnlyCollection<long> customerIds)
    {
        // 在内存取最大值：避免不同 EF Provider 对 DefaultIfEmpty()+Max() 的翻译差异。
        var timestamps = await _db.UserCustomers
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(uc => uc.UserId == userId)
            .Select(uc => uc.UpdatedAt)
            .ToListAsync();

        var updatedAt = timestamps.Count == 0 ? DateTime.MinValue : timestamps.Max();
        return new ManagedCustomerGrantDto(userId, customerIds.ToArray(), updatedAt);
    }
}

/// <summary>分配授权客户请求体（全量替换语义）。</summary>
public sealed record AssignManagedCustomersRequest(long[] CustomerIds);

/// <summary>授权客户回显契约（分配变更即时生效，前端 M09 可直接消费）。</summary>
public sealed record ManagedCustomerGrantDto(long UserId, long[] CustomerIds, DateTime UpdatedAt);
