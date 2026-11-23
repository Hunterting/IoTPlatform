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
/// 网络隧道管理控制器
/// </summary>
[ApiController]
[Route("api/v1/tunnels")]
[PermissionAuthorize(Permissions.MANAGE_NETWORK_TUNNEL)]
public class TunnelsController : ControllerBase
{
    private readonly ITunnelService _tunnelService;
    private readonly AppDbContext _db;
    private readonly ICustomerScopeService _scope;
    private readonly ICustomerScopeGuard _scopeGuard;

    public TunnelsController(
        ITunnelService tunnelService,
        AppDbContext db,
        ICustomerScopeService scope,
        ICustomerScopeGuard scopeGuard)
    {
        _tunnelService = tunnelService;
        _db = db;
        _scope = scope;
        _scopeGuard = scopeGuard;
    }

    private CancellationToken RequestAborted =>
        ControllerContext?.HttpContext?.RequestAborted ?? CancellationToken.None;

    /// <summary>
    /// 【B1/M4】隧道 id → AppCode（IgnoreQueryFilters 点查）→ CustomerId → 统一范围守卫。
    /// Query(null) 零过滤不能兜底，必须在连接/断开之前显式判定。
    /// </summary>
    private async Task EnsureTunnelAccessAsync(long id)
    {
        var appCode = await _db.Tunnels
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(t => t.Id == id)
            .Select(t => t.AppCode)
            .FirstOrDefaultAsync(RequestAborted);

        if (appCode == null)
        {
            throw new NotFoundException("隧道不存在");
        }

        var customerId = await _scope.ResolveCustomerIdByAppCodeAsync(appCode, RequestAborted);
        var scope = await _scope.ResolveAsync(User, RequestAborted);
        _scopeGuard.AssertScope(scope, customerId, appCode);
    }

    /// <summary>
    /// 获取隧道列表
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResponse<TunnelDto>>>> GetTunnels(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? keyword = null)
    {
        try
        {
            var appCode = User.FindFirst("AppCode")?.Value;
            var listScope = await this.ResolveListScopeAsync(_scope);
            var result = await _tunnelService.GetTunnelsAsync(page, pageSize, keyword, appCode, listScope.CustomerIds, listScope.AppCodes);
            return ApiResponse<PagedResponse<TunnelDto>>.Success(result);
        }
        catch (Exception ex)
        {
            return ApiResponse<PagedResponse<TunnelDto>>.Error(ex.Message);
        }
    }

    /// <summary>
    /// 获取隧道详情
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<ApiResponse<TunnelDto>>> GetTunnel(long id)
    {
            // B4/M6：先点查权威归属并按 scope 三分支判定；越权 HTTP 200 + Code=403。
            if (!await this.CanReadDetailAsync<IoTPlatform.Models.Tunnel>(id, false))
            {
                return Ok(ApiResponse<TunnelDto>.Fail(403, "无权访问该记录"));
            }

        try
        {
            var appCode = User.FindFirst("AppCode")?.Value;
            var result = await _tunnelService.GetTunnelAsync(id, appCode);
            if (result == null)
            {
                return Ok(ApiResponse.NotFound("隧道不存在"));
            }

            return ApiResponse<TunnelDto>.Success(result);
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse.Error(ex.Message));
        }
    }

    /// <summary>
    /// 创建隧道
    /// </summary>
    [HttpPost]
    [PermissionAuthorize(Permissions.MANAGE_TUNNEL_CONFIG)]
    public async Task<ActionResult<ApiResponse<TunnelDto>>> CreateTunnel([FromBody] CreateTunnelRequest request)
    {
        try
        {
            var result = await _tunnelService.CreateTunnelAsync(request);
            return ApiResponse<TunnelDto>.Success(result, "隧道创建成功");
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
    /// 更新隧道
    /// </summary>
    [HttpPut("{id}")]
    [PermissionAuthorize(Permissions.MANAGE_TUNNEL_CONFIG)]
    public async Task<ActionResult<ApiResponse<TunnelDto>>> UpdateTunnel(long id, [FromBody] UpdateTunnelRequest request)
    {
        try
        {
            await EnsureTunnelAccessAsync(id);
            var appCode = User.FindFirst("AppCode")?.Value;
            var result = await _tunnelService.UpdateTunnelAsync(id, request, appCode);
            return ApiResponse<TunnelDto>.Success(result, "隧道更新成功");
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
    /// 删除隧道
    /// </summary>
    [HttpDelete("{id}")]
    [PermissionAuthorize(Permissions.MANAGE_TUNNEL_CONFIG)]
    public async Task<ActionResult<ApiResponse>> DeleteTunnel(long id)
    {
        try
        {
            await EnsureTunnelAccessAsync(id);
            var appCode = User.FindFirst("AppCode")?.Value;
            await _tunnelService.DeleteTunnelAsync(id, appCode);
            return ApiResponse.Success("隧道删除成功");
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
    /// 连接隧道
    /// </summary>
    [HttpPost("{id}/connect")]
    [PermissionAuthorize(Permissions.MANAGE_TUNNEL_CONFIG)]
    public async Task<ActionResult<ApiResponse>> ConnectTunnel(long id)
    {
        try
        {
            // 【M4】越权时 0 次连接副作用。
            await EnsureTunnelAccessAsync(id);
            var appCode = User.FindFirst("AppCode")?.Value;
            await _tunnelService.ConnectTunnelAsync(id, appCode);
            return ApiResponse.Success("隧道连接成功");
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
    /// 断开隧道
    /// </summary>
    [HttpPost("{id}/disconnect")]
    [PermissionAuthorize(Permissions.MANAGE_TUNNEL_CONFIG)]
    public async Task<ActionResult<ApiResponse>> DisconnectTunnel(long id)
    {
        try
        {
            // 【M4】越权时 0 次断开副作用。
            await EnsureTunnelAccessAsync(id);
            var appCode = User.FindFirst("AppCode")?.Value;
            await _tunnelService.DisconnectTunnelAsync(id, appCode);
            return ApiResponse.Success("隧道断开成功");
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
