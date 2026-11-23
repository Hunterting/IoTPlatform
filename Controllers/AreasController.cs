using IoTPlatform.Configuration;
using IoTPlatform.DTOs.Requests;
using IoTPlatform.DTOs.Responses;
using IoTPlatform.Filters;
using IoTPlatform.Helpers;
using IoTPlatform.Services;
using IoTPlatform.Services.Guards;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace IoTPlatform.Controllers;

/// <summary>
/// 区域管理控制器
/// </summary>
[ApiController]
[Route("api/v1/areas")]
[PermissionAuthorize(Permissions.VIEW_AREAS)]
public class AreasController : ControllerBase
{
    private readonly IAreaService _areaService;
    private readonly ICustomerScopeService _customerScope;

    public AreasController(IAreaService areaService, ICustomerScopeService customerScope)
    {
        _areaService = areaService;
        _customerScope = customerScope;
    }

    /// <summary>
    /// 【多客户授权 T04 / §5.5】按 ScopeMode 解析区域取数入参：
    /// 超管 All → appCode=null + customerIds=null（全量）；
    /// admin Multi → appCode=null + 授权客户集合（DB 权威）；
    /// 下级 Single → 原单 appCode + customerIds=null（逐字零回归）。
    /// 注意 All 的空集合表示"不过滤"，绝不可按集合空否分支。
    /// </summary>
    private async Task<(string? AppCode, IReadOnlyList<long>? CustomerIds)> ResolveAreaScopeAsync()
    {
        var scope = await _customerScope.ResolveAsync(User);
        if (scope.Mode == CustomerScopeMode.All)
        {
            return (null, null);
        }

        if (scope.Mode == CustomerScopeMode.Multi)
        {
            return (null, scope.CustomerIds);
        }

        return (User.FindFirst("AppCode")?.Value, null);
    }

    /// <summary>
    /// 获取区域列表
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResponse<AreaDto>>>> GetAreas(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? keyword = null)
    {
        try
        {
            var (appCode, customerIds) = await ResolveAreaScopeAsync();
            var role = User.FindFirst(ClaimTypes.Role)?.Value;
            var allowedAreaIds = GetAllowedAreaIds();

            var result = await _areaService.GetAreasAsync(page, pageSize, keyword, appCode, role, allowedAreaIds, customerIds);
            return ApiResponse<PagedResponse<AreaDto>>.Success(result);
        }
        catch (Exception ex)
        {
            return ApiResponse<PagedResponse<AreaDto>>.Error(ex.Message);
        }
    }

    /// <summary>
    /// 获取区域树
    /// </summary>
    [HttpGet("tree")]
    public async Task<ActionResult<ApiResponse<List<AreaTreeNodeDto>>>> GetAreaTree()
    {
        try
        {
            var (appCode, customerIds) = await ResolveAreaScopeAsync();
            var role = User.FindFirst(ClaimTypes.Role)?.Value;
            var allowedAreaIds = GetAllowedAreaIds();

            var result = await _areaService.GetAreaTreeAsync(appCode, role, allowedAreaIds, customerIds);
            return ApiResponse<List<AreaTreeNodeDto>>.Success(result);
        }
        catch (Exception ex)
        {
            return ApiResponse<List<AreaTreeNodeDto>>.Error(ex.Message);
        }
    }

    /// <summary>
    /// 获取子区域列表
    /// </summary>
    [HttpGet("{parentId}/children")]
    public async Task<ActionResult<ApiResponse<List<AreaDto>>>> GetChildAreas(long parentId)
    {
        try
        {
            var (appCode, customerIds) = await ResolveAreaScopeAsync();
            var allowedAreaIds = GetAllowedAreaIds();

            var result = await _areaService.GetChildAreasAsync(parentId, appCode, allowedAreaIds, customerIds);
            return ApiResponse<List<AreaDto>>.Success(result);
        }
        catch (Exception ex)
        {
            return ApiResponse<List<AreaDto>>.Error(ex.Message);
        }
    }

    /// <summary>
    /// 获取区域详情
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<ApiResponse<AreaDto>>> GetArea(long id)
    {
        try
        {
            var (appCode, customerIds) = await ResolveAreaScopeAsync();
            var role = User.FindFirst(ClaimTypes.Role)?.Value;
            var allowedAreaIds = GetAllowedAreaIds();

            var result = await _areaService.GetAreaAsync(id, appCode, role, allowedAreaIds, customerIds);
            if (result == null)
            {
                var response = ApiResponse.NotFound("区域不存在");
            return Ok(response);
            }

            return ApiResponse<AreaDto>.Success(result);
        }
        catch (Exception ex)
        {
            var response = ApiResponse.Error(ex.Message);
            return Ok(response);
        }
    }

    /// <summary>
    /// 创建区域
    /// </summary>
    [HttpPost]
    [PermissionAuthorize(Permissions.CREATE_AREAS)]
    public async Task<ActionResult<ApiResponse<AreaDto>>> CreateArea([FromBody] CreateAreaRequest request)
    {
        try
        {
            // 【M1 / B2 QA 遗留】admin 自身 AppCode=null，旧软校验恒真跳过 → 可在任意客户下建区域。
            // 这里按 DB 权威授权集合校验目标客户，且在任何写库之前完成。
            if (Roles.ADMIN == User.FindFirst(ClaimTypes.Role)?.Value)
            {
                if (!request.CustomerId.HasValue)
                {
                    return Ok(ApiResponse<AreaDto>.BadRequest("租户管理员必须在授权客户下创建区域。"));
                }
                if (!await _customerScope.CanAccessCustomerAsync(User, request.CustomerId.Value))
                {
                    return Ok(ApiResponse<AreaDto>.Forbidden("无权在该客户下创建区域"));
                }

                var resolvedAppCode = await _customerScope.ResolveAppCodeByCustomerIdAsync(request.CustomerId.Value);
                if (string.IsNullOrWhiteSpace(resolvedAppCode))
                {
                    return Ok(ApiResponse<AreaDto>.BadRequest("目标客户缺少 AppCode"));
                }

                var verifiedResult = await _areaService.CreateAreaAsync(
                    request, resolvedAppCode, customerScopeVerified: true);
                return ApiResponse<AreaDto>.Success(verifiedResult, "区域创建成功");
            }

            var result = await _areaService.CreateAreaAsync(request);
            return ApiResponse<AreaDto>.Success(result, "区域创建成功");
        }
        catch (UnauthorizedAccessException ex)
        {
            return Ok(ApiResponse<AreaDto>.Forbidden(ex.Message));
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
    /// 更新区域
    /// </summary>
    [HttpPut("{id}")]
    [PermissionAuthorize(Permissions.UPDATE_AREAS)]
    public async Task<ActionResult<ApiResponse<AreaDto>>> UpdateArea(long id, [FromBody] UpdateAreaRequest request)
    {
        try
        {
            var appCode = User.FindFirst("AppCode")?.Value;

            // B5/M8：显式解析范围（DB 权威）后下推给 Service 做三分支判据，禁止 AsyncLocal。
            var scope = await _customerScope.ResolveAsync(User);
            var result = await _areaService.UpdateAreaAsync(id, request, appCode, scope, customerScopeVerified: true);
            return ApiResponse<AreaDto>.Success(result, "区域更新成功");
        }
        catch (ForbiddenAccessException ex)
        {
            return Ok(ApiResponse<AreaDto>.Forbidden(ex.Message));
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
    /// 删除区域
    /// </summary>
    [HttpDelete("{id}")]
    [PermissionAuthorize(Permissions.DELETE_AREAS)]
    public async Task<ActionResult<ApiResponse>> DeleteArea(long id)
    {
        try
        {
            var appCode = User.FindFirst("AppCode")?.Value;

            // B5/M8：删除前先按权威范围判定归属；集合外客户仍拒绝。
            var scope = await _customerScope.ResolveAsync(User);
            await _areaService.DeleteAreaAsync(id, appCode, scope, customerScopeVerified: true);
            return ApiResponse.Success("区域删除成功");
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
    /// 获取用户允许的区域ID列表
    /// </summary>
    private List<long>? GetAllowedAreaIds()
    {
        var allowedAreaIdsClaim = User.FindFirst("AllowedAreaIds")?.Value;
        if (string.IsNullOrEmpty(allowedAreaIdsClaim))
            return null;

        return allowedAreaIdsClaim.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(id => long.Parse(id.Trim()))
            .ToList();
    }
}
