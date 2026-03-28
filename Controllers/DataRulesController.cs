using IoTPlatform.DTOs.Requests;
using IoTPlatform.DTOs.Responses;
using IoTPlatform.Filters;
using IoTPlatform.Helpers;
using IoTPlatform.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using IoTPlatform.Configuration;

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

    public DataRulesController(IDataRuleService dataRuleService)
    {
        _dataRuleService = dataRuleService;
    }

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
            var result = await _dataRuleService.GetDataRulesAsync(page, pageSize, keyword, ruleType, appCode);
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
        try
        {
            var appCode = User.FindFirst("AppCode")?.Value;
            var result = await _dataRuleService.GetDataRuleAsync(id, appCode);
            if (result == null)
            {
                return Ok(ApiResponse<DataRuleDto>.NotFound("数据规则不存在"));
            }

            return ApiResponse<DataRuleDto>.Success(result);
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse<DataRuleDto>.Error(ex.Message));
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
            var result = await _dataRuleService.CreateDataRuleAsync(request);
            return ApiResponse<DataRuleDto>.Success(result, "数据规则创建成功");
        }
        catch (InvalidOperationException ex)
        {
            return Ok(ApiResponse<DataRuleDto>.BadRequest(ex.Message));
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse<DataRuleDto>.Error(ex.Message));
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
            var result = await _dataRuleService.UpdateDataRuleAsync(id, request, appCode);
            return ApiResponse<DataRuleDto>.Success(result, "数据规则更新成功");
        }
        catch (InvalidOperationException ex)
        {
            return Ok(ApiResponse<DataRuleDto>.BadRequest(ex.Message));
        }
        catch (UnauthorizedAccessException ex)
        {
            return Ok(ApiResponse<DataRuleDto>.Forbidden(ex.Message));
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse<DataRuleDto>.Error(ex.Message));
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
            await _dataRuleService.DeleteDataRuleAsync(id, appCode);
            return ApiResponse.Success("数据规则删除成功");
        }
        catch (InvalidOperationException ex)
        {
            return Ok(ApiResponse.BadRequest(ex.Message));
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
