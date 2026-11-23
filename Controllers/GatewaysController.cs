using IoTPlatform.Data;
using IoTPlatform.DTOs.Requests;
using IoTPlatform.DTOs.Responses;
using IoTPlatform.Filters;
using IoTPlatform.Helpers;
using IoTPlatform.Services;
using IoTPlatform.Services.Guards;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using IoTPlatform.Configuration;
using Microsoft.EntityFrameworkCore;

namespace IoTPlatform.Controllers;

/// <summary>
/// 网关管理控制器
/// </summary>
[ApiController]
[Route("api/v1/gateways")]
[PermissionAuthorize(Permissions.MANAGE_PROTOCOL_GATEWAY)]
public class GatewaysController : ControllerBase
{
    private readonly IGatewayService _gatewayService;
    private readonly AppDbContext _db;
    private readonly ICustomerScopeService _scope;
    private readonly ICustomerScopeGuard _scopeGuard;

    public GatewaysController(
        IGatewayService gatewayService,
        AppDbContext db,
        ICustomerScopeService scope,
        ICustomerScopeGuard scopeGuard)
    {
        _gatewayService = gatewayService;
        _db = db;
        _scope = scope;
        _scopeGuard = scopeGuard;
    }

    private CancellationToken RequestAborted =>
        ControllerContext?.HttpContext?.RequestAborted ?? CancellationToken.None;

    /// <summary>
    /// 【B1/M4】网关 id → AppCode（IgnoreQueryFilters 点查）→ CustomerId → 统一范围守卫。
    /// 判定必须在启停/改删等任何副作用之前；拿不到归属即 fail-closed。
    /// </summary>
    private async Task EnsureGatewayAccessAsync(long id)
    {
        var appCode = await _db.Gateways
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(g => g.Id == id)
            .Select(g => g.AppCode)
            .FirstOrDefaultAsync(RequestAborted);

        if (appCode == null)
        {
            throw new NotFoundException("网关不存在");
        }

        var customerId = await _scope.ResolveCustomerIdByAppCodeAsync(appCode, RequestAborted);
        var scope = await _scope.ResolveAsync(User, RequestAborted);
        _scopeGuard.AssertScope(scope, customerId, appCode);
    }

    /// <summary>
    /// 获取网关列表
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResponse<GatewayDto>>>> GetGateways(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? keyword = null)
    {
        try
        {
            var appCode = User.FindFirst("AppCode")?.Value;
            var listScope = await this.ResolveListScopeAsync(_scope);
            var result = await _gatewayService.GetGatewaysAsync(page, pageSize, keyword, appCode, listScope.CustomerIds, listScope.AppCodes);
            return ApiResponse<PagedResponse<GatewayDto>>.Success(result);
        }
        catch (Exception ex)
        {
            return ApiResponse<PagedResponse<GatewayDto>>.Error(ex.Message);
        }
    }

    /// <summary>
    /// 获取网关详情
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<ApiResponse<GatewayDto>>> GetGateway(long id)
    {
            // B4/M6：先点查权威归属并按 scope 三分支判定；越权 HTTP 200 + Code=403。
            if (!await this.CanReadDetailAsync<IoTPlatform.Models.Gateway>(id, false))
            {
                return Ok(ApiResponse<GatewayDto>.Fail(403, "无权访问该记录"));
            }

        try
        {
            var appCode = User.FindFirst("AppCode")?.Value;
            var result = await _gatewayService.GetGatewayAsync(id, appCode);
            if (result == null)
            {
                return Ok(ApiResponse.NotFound("网关不存在"));
            }

            return ApiResponse<GatewayDto>.Success(result);
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse.Error(ex.Message));
        }
    }

    /// <summary>
    /// 创建网关
    /// </summary>
    [HttpPost]
    [PermissionAuthorize(Permissions.MANAGE_GATEWAY_CONFIG)]
    public async Task<ActionResult<ApiResponse<GatewayDto>>> CreateGateway([FromBody] CreateGatewayRequest request)
    {
        try
        {
            var result = await _gatewayService.CreateGatewayAsync(request);
            return ApiResponse<GatewayDto>.Success(result, "网关创建成功");
        }
        catch (InvalidOperationException ex)
        {
            return Ok(ApiResponse.BadRequest(ex.Message));
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse.Error(ex.Message));
        }
    }

    /// <summary>
    /// 更新网关
    /// </summary>
    [HttpPut("{id}")]
    [PermissionAuthorize(Permissions.MANAGE_GATEWAY_CONFIG)]
    public async Task<ActionResult<ApiResponse<GatewayDto>>> UpdateGateway(long id, [FromBody] UpdateGatewayRequest request)
    {
        try
        {
            await EnsureGatewayAccessAsync(id);
            var appCode = User.FindFirst("AppCode")?.Value;
            var result = await _gatewayService.UpdateGatewayAsync(id, request, appCode);
            return ApiResponse<GatewayDto>.Success(result, "网关更新成功");
        }
        catch (InvalidOperationException ex)
        {
            return Ok(ApiResponse.BadRequest(ex.Message));
        }
        catch (ForbiddenAccessException ex)
        {
            return Ok(ApiResponse.Forbidden(ex.Message));
        }
        catch (UnauthorizedAccessException ex)
        {
            return Ok(ApiResponse.Forbidden(ex.Message));
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse.Error(ex.Message));
        }
    }

    /// <summary>
    /// 删除网关
    /// </summary>
    [HttpDelete("{id}")]
    [PermissionAuthorize(Permissions.MANAGE_GATEWAY_CONFIG)]
    public async Task<ActionResult<ApiResponse>> DeleteGateway(long id)
    {
        try
        {
            await EnsureGatewayAccessAsync(id);
            var appCode = User.FindFirst("AppCode")?.Value;
            await _gatewayService.DeleteGatewayAsync(id, appCode);
            return ApiResponse.Success("网关删除成功");
        }
        catch (InvalidOperationException ex)
        {
            return Ok(ApiResponse.BadRequest(ex.Message));
        }
        catch (ForbiddenAccessException ex)
        {
            return Ok(ApiResponse.Forbidden(ex.Message));
        }
        catch (UnauthorizedAccessException ex)
        {
            return Ok(ApiResponse.Forbidden(ex.Message));
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse.Error(ex.Message));
        }
    }

    /// <summary>
    /// 启动网关
    /// </summary>
    [HttpPost("{id}/start")]
    [PermissionAuthorize(Permissions.MANAGE_GATEWAY_CONFIG)]
    public async Task<ActionResult<ApiResponse>> StartGateway(long id)
    {
        try
        {
            // 【M4】越权时绝不进入 GatewayService.StartGatewayAsync（0 次启动副作用）。
            await EnsureGatewayAccessAsync(id);
            var appCode = User.FindFirst("AppCode")?.Value;
            await _gatewayService.StartGatewayAsync(id, appCode);
            return ApiResponse.Success("网关启动成功");
        }
        catch (InvalidOperationException ex)
        {
            return Ok(ApiResponse.BadRequest(ex.Message));
        }
        catch (ForbiddenAccessException ex)
        {
            return Ok(ApiResponse.Forbidden(ex.Message));
        }
        catch (UnauthorizedAccessException ex)
        {
            return Ok(ApiResponse.Forbidden(ex.Message));
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse.Error(ex.Message));
        }
    }

    /// <summary>
    /// 停止网关
    /// </summary>
    [HttpPost("{id}/stop")]
    [PermissionAuthorize(Permissions.MANAGE_GATEWAY_CONFIG)]
    public async Task<ActionResult<ApiResponse>> StopGateway(long id)
    {
        try
        {
            // 【M4】越权时绝不进入 GatewayService.StopGatewayAsync（0 次停止副作用）。
            await EnsureGatewayAccessAsync(id);
            var appCode = User.FindFirst("AppCode")?.Value;
            await _gatewayService.StopGatewayAsync(id, appCode);
            return ApiResponse.Success("网关停止成功");
        }
        catch (InvalidOperationException ex)
        {
            return Ok(ApiResponse.BadRequest(ex.Message));
        }
        catch (ForbiddenAccessException ex)
        {
            return Ok(ApiResponse.Forbidden(ex.Message));
        }
        catch (UnauthorizedAccessException ex)
        {
            return Ok(ApiResponse.Forbidden(ex.Message));
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse.Error(ex.Message));
        }
    }
}
