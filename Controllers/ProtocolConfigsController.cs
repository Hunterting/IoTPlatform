using IoTPlatform.DTOs.Requests;
using IoTPlatform.DTOs.Responses;
using IoTPlatform.Filters;
using IoTPlatform.Helpers;
using IoTPlatform.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using IoTPlatform.Configuration;
using IoTPlatform.Services.Guards;

namespace IoTPlatform.Controllers;

/// <summary>
/// 协议配置管理控制器
/// </summary>
[ApiController]
[Route("api/v1/protocol-configs")]
[PermissionAuthorize(Permissions.MANAGE_PROTOCOLS)]
public class ProtocolConfigsController : ControllerBase
{
    private readonly IProtocolConfigService _protocolConfigService;
    private readonly ICustomerScopeService? _customerScope;

    public ProtocolConfigsController(
        IProtocolConfigService protocolConfigService,
        ICustomerScopeService? customerScope = null)
    {
        _protocolConfigService = protocolConfigService;
        _customerScope = customerScope;
    }

    /// <summary>下推给 Service 双保险的范围；缺少范围服务时返回 null，由守卫 fail-closed。</summary>
    private async Task<CustomerScope?> ResolveScopeAsync() =>
        _customerScope == null ? null : await _customerScope.ResolveAsync(User);

    /// <summary>
    /// 租户过滤码：普通租户取自身 AppCode；超管（无 AppCode / system）返回 null 表示看全部。
    /// </summary>
    private string? TenantAppCode
    {
        get
        {
            var c = User.FindFirst("AppCode")?.Value;
            return string.IsNullOrEmpty(c) || c == "system" ? null : c;
        }
    }

    /// <summary>
    /// 获取协议配置列表
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResponse<ProtocolConfigDto>>>> GetProtocolConfigs(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? keyword = null,
        [FromQuery] string? type = null)
    {
        try
        {
            var appCode = TenantAppCode;
            var listScope = await this.ResolveListScopeAsync(_customerScope);
            var result = await _protocolConfigService.GetProtocolConfigsAsync(page, pageSize, keyword, type, appCode, listScope.CustomerIds, listScope.AppCodes);
            return ApiResponse<PagedResponse<ProtocolConfigDto>>.Success(result);
        }
        catch (Exception ex)
        {
            return ApiResponse<PagedResponse<ProtocolConfigDto>>.Error(ex.Message);
        }
    }

    /// <summary>
    /// 获取协议配置详情
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<ApiResponse<ProtocolConfigDto>>> GetProtocolConfig(long id)
    {
            // B4/M6：先点查权威归属并按 scope 三分支判定；越权 HTTP 200 + Code=403。
            if (!await this.CanReadDetailAsync<IoTPlatform.Models.ProtocolConfig>(id, true))
            {
                return Ok(ApiResponse<ProtocolConfigDto>.Fail(403, "无权访问该记录"));
            }

        try
        {
            var appCode = TenantAppCode;
            var result = await _protocolConfigService.GetProtocolConfigAsync(id, appCode);
            if (result == null)
            {
                var response = ApiResponse.NotFound("协议配置不存在");
            return Ok(response);
            }

            return ApiResponse<ProtocolConfigDto>.Success(result);
        }
        catch (Exception ex)
        {
            var response = ApiResponse.Error(ex.Message);
            return Ok(response);
        }
    }

    /// <summary>
    /// 创建协议配置
    /// </summary>
    [HttpPost]
    [PermissionAuthorize(Permissions.MANAGE_PROTOCOL_CONFIG)]
    public async Task<ActionResult<ApiResponse<ProtocolConfigDto>>> CreateProtocolConfig([FromBody] CreateProtocolConfigRequest request)
    {
        try
        {
            // M2：请求体 AppCode 只是待校验目标；admin 必须经 DB 授权集合校验后才能落库，
            // 归属 AppCode 一律由服务端派生（禁止 `?? request.AppCode` 回退）。
            var target = await WriteTargetResolver.ResolveAsync(_customerScope, User, request.AppCode);
            if (!target.Allowed)
            {
                return Ok(target.ErrorCode == 400
                    ? ApiResponse.BadRequest(target.ErrorMessage)
                    : ApiResponse.Forbidden(target.ErrorMessage));
            }

            var result = await _protocolConfigService.CreateProtocolConfigAsync(
                request, target.AppCode, await ResolveScopeAsync());
            return ApiResponse<ProtocolConfigDto>.Success(result, "协议配置创建成功");
        }
        catch (ForbiddenAccessException ex)
        {
            return Ok(ApiResponse.Forbidden(ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            var response = ApiResponse.BadRequest(ex.Message);
            return Ok(response);
        }
        catch (Exception ex)
        {
            var response = ApiResponse.Error(ex.Message);
            return Ok(response);
        }
    }

    /// <summary>
    /// 更新协议配置
    /// </summary>
    [HttpPut("{id}")]
    [PermissionAuthorize(Permissions.MANAGE_PROTOCOL_CONFIG)]
    public async Task<ActionResult<ApiResponse<ProtocolConfigDto>>> UpdateProtocolConfig(long id, [FromBody] UpdateProtocolConfigRequest request)
    {
        try
        {
            var appCode = TenantAppCode;
            var result = await _protocolConfigService.UpdateProtocolConfigAsync(
                id, request, appCode, await ResolveScopeAsync());
            return ApiResponse<ProtocolConfigDto>.Success(result, "协议配置更新成功");
        }
        catch (ForbiddenAccessException ex)
        {
            return Ok(ApiResponse.Forbidden(ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            var response = ApiResponse.BadRequest(ex.Message);
            return Ok(response);
        }
        catch (UnauthorizedAccessException ex)
        {
            var response = ApiResponse.Forbidden(ex.Message);
            return Ok(response);
        }
        catch (Exception ex)
        {
            var response = ApiResponse.Error(ex.Message);
            return Ok(response);
        }
    }

    /// <summary>
    /// 删除协议配置
    /// </summary>
    [HttpDelete("{id}")]
    [PermissionAuthorize(Permissions.MANAGE_PROTOCOL_CONFIG)]
    public async Task<ActionResult<ApiResponse>> DeleteProtocolConfig(long id)
    {
        try
        {
            var appCode = TenantAppCode;
            await _protocolConfigService.DeleteProtocolConfigAsync(id, appCode, await ResolveScopeAsync());
            return ApiResponse.Success("协议配置删除成功");
        }
        catch (ForbiddenAccessException ex)
        {
            return Ok(ApiResponse.Forbidden(ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            var response = ApiResponse.BadRequest(ex.Message);
            return Ok(response);
        }
        catch (UnauthorizedAccessException ex)
        {
            var response = ApiResponse.Forbidden(ex.Message);
            return Ok(response);
        }
        catch (Exception ex)
        {
            var response = ApiResponse.Error(ex.Message);
            return Ok(response);
        }
    }

    /// <summary>
    /// 启动协议
    /// </summary>
    [HttpPost("{id}/start")]
    [PermissionAuthorize(Permissions.MANAGE_PROTOCOL_GATEWAY)]
    public async Task<ActionResult<ApiResponse>> StartProtocol(long id)
    {
        try
        {
            var appCode = TenantAppCode;
            await _protocolConfigService.StartProtocolAsync(id, appCode, await ResolveScopeAsync());
            return ApiResponse.Success("协议启动成功");
        }
        catch (ForbiddenAccessException ex)
        {
            return Ok(ApiResponse.Forbidden(ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            var response = ApiResponse.BadRequest(ex.Message);
            return Ok(response);
        }
        catch (UnauthorizedAccessException ex)
        {
            var response = ApiResponse.Forbidden(ex.Message);
            return Ok(response);
        }
        catch (Exception ex)
        {
            var response = ApiResponse.Error(ex.Message);
            return Ok(response);
        }
    }

    /// <summary>
    /// 停止协议
    /// </summary>
    [HttpPost("{id}/stop")]
    [PermissionAuthorize(Permissions.MANAGE_PROTOCOL_GATEWAY)]
    public async Task<ActionResult<ApiResponse>> StopProtocol(long id)
    {
        try
        {
            var appCode = TenantAppCode;
            await _protocolConfigService.StopProtocolAsync(id, appCode, await ResolveScopeAsync());
            return ApiResponse.Success("协议停止成功");
        }
        catch (ForbiddenAccessException ex)
        {
            return Ok(ApiResponse.Forbidden(ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            var response = ApiResponse.BadRequest(ex.Message);
            return Ok(response);
        }
        catch (UnauthorizedAccessException ex)
        {
            var response = ApiResponse.Forbidden(ex.Message);
            return Ok(response);
        }
        catch (Exception ex)
        {
            var response = ApiResponse.Error(ex.Message);
            return Ok(response);
        }
    }
}
