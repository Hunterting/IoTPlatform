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
/// 数据规则管理控制器
/// </summary>
[ApiController]
[Route("api/v1/data-rules")]
[PermissionAuthorize(Permissions.VIEW_RULE_ENGINE)]
public class DataRulesController : ControllerBase
{
    private readonly IDataRuleService _dataRuleService;
    private readonly ICustomerScopeService? _customerScope;

    public DataRulesController(IDataRuleService dataRuleService, ICustomerScopeService? customerScope = null)
    {
        _dataRuleService = dataRuleService;
        _customerScope = customerScope;
    }

    /// <summary>下推给 Service 双保险的范围；缺少范围服务时返回 null，由守卫 fail-closed。</summary>
    private async Task<CustomerScope?> ResolveScopeAsync() =>
        _customerScope == null ? null : await _customerScope.ResolveAsync(User);

    /// <summary>
    /// 获取数据规则列表
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResponse<DataRuleDto>>>> GetDataRules(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? keyword = null,
        [FromQuery] string? ruleType = null)
    {
        try
        {
            var appCode = User.FindFirst("AppCode")?.Value;
            var listScope = await this.ResolveListScopeAsync(_customerScope);
            var result = await _dataRuleService.GetDataRulesAsync(page, pageSize, keyword, ruleType, appCode, listScope.CustomerIds, listScope.AppCodes);
            return ApiResponse<PagedResponse<DataRuleDto>>.Success(result);
        }
        catch (Exception ex)
        {
            return ApiResponse<PagedResponse<DataRuleDto>>.Error(ex.Message);
        }
    }

    /// <summary>
    /// 获取数据规则详情
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<ApiResponse<DataRuleDto>>> GetDataRule(long id)
    {
            // B4/M6：先点查权威归属并按 scope 三分支判定；越权 HTTP 200 + Code=403。
            if (!await this.CanReadDetailAsync<IoTPlatform.Models.DataRule>(id, true))
            {
                return Ok(ApiResponse<DataRuleDto>.Fail(403, "无权访问该记录"));
            }

        try
        {
            var appCode = User.FindFirst("AppCode")?.Value;
            var result = await _dataRuleService.GetDataRuleAsync(id, appCode);
            if (result == null)
            {
                var response = ApiResponse.NotFound("数据规则不存在");
            return Ok(response);
            }

            return ApiResponse<DataRuleDto>.Success(result);
        }
        catch (Exception ex)
        {
            var response = ApiResponse.Error(ex.Message);
            return Ok(response);
        }
    }

    /// <summary>
    /// 创建数据规则
    /// </summary>
    [HttpPost]
    [PermissionAuthorize(Permissions.MANAGE_RULES)]
    public async Task<ActionResult<ApiResponse<DataRuleDto>>> CreateDataRule([FromBody] CreateDataRuleRequest request)
    {
        try
        {
            var target = await WriteTargetResolver.ResolveAsync(_customerScope, User, request.AppCode);
            if (!target.Allowed)
            {
                return Ok(target.ErrorCode == 400
                    ? ApiResponse.BadRequest(target.ErrorMessage)
                    : ApiResponse.Forbidden(target.ErrorMessage));
            }

            var result = await _dataRuleService.CreateDataRuleAsync(request, await ResolveScopeAsync());
            return ApiResponse<DataRuleDto>.Success(result, "数据规则创建成功");
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
    /// 更新数据规则
    /// </summary>
    [HttpPut("{id}")]
    [PermissionAuthorize(Permissions.MANAGE_RULES)]
    public async Task<ActionResult<ApiResponse<DataRuleDto>>> UpdateDataRule(long id, [FromBody] UpdateDataRuleRequest request)
    {
        try
        {
            var appCode = User.FindFirst("AppCode")?.Value;
            var result = await _dataRuleService.UpdateDataRuleAsync(id, request, appCode, await ResolveScopeAsync());
            return ApiResponse<DataRuleDto>.Success(result, "数据规则更新成功");
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
    /// 删除数据规则
    /// </summary>
    [HttpDelete("{id}")]
    [PermissionAuthorize(Permissions.MANAGE_RULES)]
    public async Task<ActionResult<ApiResponse>> DeleteDataRule(long id)
    {
        try
        {
            var appCode = User.FindFirst("AppCode")?.Value;
            await _dataRuleService.DeleteDataRuleAsync(id, appCode, await ResolveScopeAsync());
            return ApiResponse.Success("数据规则删除成功");
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
