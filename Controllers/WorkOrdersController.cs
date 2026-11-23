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
/// 工单管理控制器
/// </summary>
[ApiController]
[Route("api/v1/work-orders")]
[PermissionAuthorize(Permissions.VIEW_WORK_ORDERS)]
public class WorkOrdersController : ControllerBase
{
    private readonly IWorkOrderService _workOrderService;
    private readonly ICustomerScopeService _customerScope;

    public WorkOrdersController(IWorkOrderService workOrderService, ICustomerScopeService customerScope)
    {
        _workOrderService = workOrderService;
        _customerScope = customerScope ?? throw new ArgumentNullException(nameof(customerScope));
    }

    /// <summary>
    /// 租户过滤码：普通租户取自身 AppCode；超管（无 AppCode / system）返回 null 表示看全部。
    /// SwitchCustomer 后超管 token 带目标租户 AppCode，此时按该租户过滤。
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
    /// 获取工单列表
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResponse<WorkOrderDto>>>> GetWorkOrders(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? status = null,
        [FromQuery] string? type = null,
        [FromQuery] string? priority = null)
    {
        try
        {
            var appCode = TenantAppCode;
            // 【租户隔离】只按 JWT AppCode 隔离，不叠加区域过滤。
            // 服务层 allowedAreaIds 语义为 AreaId.HasValue && Contains(...)，会把真实工单按区域白名单排除（与 AlertsController 同源问题）。
            var listScope = await this.ResolveListScopeAsync(_customerScope);
            var result = await _workOrderService.GetWorkOrdersAsync(page, pageSize, status, type, priority, appCode, null, listScope.CustomerIds, listScope.AppCodes);
            return ApiResponse<PagedResponse<WorkOrderDto>>.Success(result);
        }
        catch (Exception ex)
        {
            return ApiResponse<PagedResponse<WorkOrderDto>>.Error(ex.Message);
        }
    }

    /// <summary>
    /// 获取工单详情
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<ApiResponse<WorkOrderDto>>> GetWorkOrder(long id)
    {
            // B4/M6：先点查权威归属并按 scope 三分支判定；越权 HTTP 200 + Code=403。
            if (!await this.CanReadDetailAsync<IoTPlatform.Models.WorkOrder>(id, false))
            {
                return Ok(ApiResponse<WorkOrderDto>.Fail(403, "无权访问该记录"));
            }

        try
        {
            var appCode = TenantAppCode;
            // 【租户隔离】同上，详情只按 AppCode 隔离，不叠加区域过滤。
            var result = await _workOrderService.GetWorkOrderAsync(id, appCode, null);
            if (result == null)
            {
                var response = ApiResponse.NotFound("工单不存在");
            return Ok(response);
            }

            return ApiResponse<WorkOrderDto>.Success(result);
        }
        catch (Exception ex)
        {
            var response = ApiResponse.Error(ex.Message);
            return Ok(response);
        }
    }

    /// <summary>
    /// 创建工单
    /// </summary>
    [HttpPost]
    [PermissionAuthorize(Permissions.CREATE_WORK_ORDERS)]
    public async Task<ActionResult<ApiResponse<WorkOrderDto>>> CreateWorkOrder([FromBody] CreateWorkOrderRequest request)
    {
        try
        {
            // 【M1】admin 自身无 AppCode claim：必须在任何写库之前用 DB 权威授权集合判定目标客户。
            if (Roles.ADMIN == User.FindFirst(ClaimTypes.Role)?.Value)
            {
                if (!request.CustomerId.HasValue)
                {
                    return Ok(ApiResponse<WorkOrderDto>.BadRequest("租户管理员必须在授权客户下创建工单。"));
                }
                if (!await _customerScope.CanAccessCustomerAsync(User, request.CustomerId.Value))
                {
                    return Ok(ApiResponse<WorkOrderDto>.Forbidden("无权在该客户下创建工单"));
                }

                var resolvedAppCode = await _customerScope.ResolveAppCodeByCustomerIdAsync(request.CustomerId.Value);
                if (string.IsNullOrWhiteSpace(resolvedAppCode))
                {
                    return Ok(ApiResponse<WorkOrderDto>.BadRequest("目标客户缺少 AppCode"));
                }

                var verifiedResult = await _workOrderService.CreateWorkOrderAsync(
                    request, resolvedAppCode, customerScopeVerified: true);
                return ApiResponse<WorkOrderDto>.Success(verifiedResult, "工单创建成功");
            }

            // 超管 / 下级：原路径逐字不改。
            // 租户传的 appCode 会被 JWT 的 TenantAppCode 覆盖（防越权）；
            // 超管 TenantAppCode=null 时允许 request.AppCode（代租户创建）。
            var effectiveAppCode = TenantAppCode ?? request.AppCode;
            var result = await _workOrderService.CreateWorkOrderAsync(request, effectiveAppCode);
            return ApiResponse<WorkOrderDto>.Success(result, "工单创建成功");
        }
        catch (UnauthorizedAccessException ex)
        {
            return Ok(ApiResponse<WorkOrderDto>.Forbidden(ex.Message));
        }
        catch (ForbiddenAccessException ex)
        {
            return Ok(ApiResponse<WorkOrderDto>.Forbidden(ex.Message));
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
    /// 更新工单
    /// </summary>
    [HttpPut("{id}")]
    [PermissionAuthorize(Permissions.UPDATE_WORK_ORDERS)]
    public async Task<ActionResult<ApiResponse<WorkOrderDto>>> UpdateWorkOrder(long id, [FromBody] UpdateWorkOrderRequest request)
    {
        try
        {
            var result = await _workOrderService.UpdateWorkOrderAsync(id, request, TenantAppCode, User);
            return ApiResponse<WorkOrderDto>.Success(result, "工单更新成功");
        }
        catch (ForbiddenAccessException ex)
        {
            return Ok(ApiResponse<WorkOrderDto>.Forbidden(ex.Message));
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
    /// 分配工单
    /// </summary>
    [HttpPost("{id}/assign")]
    [PermissionAuthorize(Permissions.UPDATE_WORK_ORDERS)]
    public async Task<ActionResult<ApiResponse<WorkOrderDto>>> AssignWorkOrder(long id, [FromBody] AssignWorkOrderRequest request)
    {
        try
        {
            var result = await _workOrderService.AssignWorkOrderAsync(id, request.Assignee, TenantAppCode, User);
            return ApiResponse<WorkOrderDto>.Success(result, "工单分配成功");
        }
        catch (ForbiddenAccessException ex)
        {
            return Ok(ApiResponse<WorkOrderDto>.Forbidden(ex.Message));
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
    /// 开始处理工单
    /// </summary>
    [HttpPost("{id}/start")]
    [PermissionAuthorize(Permissions.UPDATE_WORK_ORDERS)]
    public async Task<ActionResult<ApiResponse<WorkOrderDto>>> StartWorkOrder(long id)
    {
        try
        {
            var result = await _workOrderService.StartWorkOrderAsync(id, TenantAppCode, User);
            return ApiResponse<WorkOrderDto>.Success(result, "工单开始处理");
        }
        catch (ForbiddenAccessException ex)
        {
            return Ok(ApiResponse<WorkOrderDto>.Forbidden(ex.Message));
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
    /// 解决工单
    /// </summary>
    [HttpPost("{id}/resolve")]
    [PermissionAuthorize(Permissions.UPDATE_WORK_ORDERS)]
    public async Task<ActionResult<ApiResponse<WorkOrderDto>>> ResolveWorkOrder(long id, [FromBody] ResolveWorkOrderRequest request)
    {
        try
        {
            var result = await _workOrderService.ResolveWorkOrderAsync(id, request.ResolveDescription, TenantAppCode, User);
            return ApiResponse<WorkOrderDto>.Success(result, "工单已解决");
        }
        catch (ForbiddenAccessException ex)
        {
            return Ok(ApiResponse<WorkOrderDto>.Forbidden(ex.Message));
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
    /// 关闭工单
    /// </summary>
    [HttpPost("{id}/close")]
    [PermissionAuthorize(Permissions.UPDATE_WORK_ORDERS)]
    public async Task<ActionResult<ApiResponse<WorkOrderDto>>> CloseWorkOrder(long id)
    {
        try
        {
            var result = await _workOrderService.CloseWorkOrderAsync(id, TenantAppCode, User);
            return ApiResponse<WorkOrderDto>.Success(result, "工单已关闭");
        }
        catch (ForbiddenAccessException ex)
        {
            return Ok(ApiResponse<WorkOrderDto>.Forbidden(ex.Message));
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
    /// 拒绝工单
    /// </summary>
    [HttpPost("{id}/reject")]
    [PermissionAuthorize(Permissions.UPDATE_WORK_ORDERS)]
    public async Task<ActionResult<ApiResponse<WorkOrderDto>>> RejectWorkOrder(long id, [FromBody] RejectWorkOrderRequest request)
    {
        try
        {
            var result = await _workOrderService.RejectWorkOrderAsync(id, request.Reason, TenantAppCode, User);
            return ApiResponse<WorkOrderDto>.Success(result, "工单已拒绝");
        }
        catch (ForbiddenAccessException ex)
        {
            return Ok(ApiResponse<WorkOrderDto>.Forbidden(ex.Message));
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
    /// 获取工单日志
    /// </summary>
    [HttpGet("{id}/logs")]
    public async Task<ActionResult<ApiResponse<List<WorkOrderLogDto>>>> GetWorkOrderLogs(long id)
    {
            // B4/M6：先点查权威归属并按 scope 三分支判定；越权 HTTP 200 + Code=403。
            if (!await this.CanReadDetailAsync<IoTPlatform.Models.WorkOrder>(id, false))
            {
                return Ok(ApiResponse<List<WorkOrderLogDto>>.Fail(403, "无权访问该记录"));
            }

        try
        {
            var result = await _workOrderService.GetWorkOrderLogsAsync(id, TenantAppCode);
            return ApiResponse<List<WorkOrderLogDto>>.Success(result);
        }
        catch (InvalidOperationException ex)
        {
            return ApiResponse<List<WorkOrderLogDto>>.BadRequest(ex.Message);
        }
        catch (Exception ex)
        {
            return ApiResponse<List<WorkOrderLogDto>>.Error(ex.Message);
        }
    }

    /// <summary>
    /// 获取工单附件
    /// </summary>
    [HttpGet("{id}/attachments")]
    public async Task<ActionResult<ApiResponse<List<WorkOrderAttachmentDto>>>> GetWorkOrderAttachments(long id)
    {
            // B4/M6：先点查权威归属并按 scope 三分支判定；越权 HTTP 200 + Code=403。
            if (!await this.CanReadDetailAsync<IoTPlatform.Models.WorkOrder>(id, false))
            {
                return Ok(ApiResponse<List<WorkOrderAttachmentDto>>.Fail(403, "无权访问该记录"));
            }

        try
        {
            var result = await _workOrderService.GetWorkOrderAttachmentsAsync(id, TenantAppCode);
            return ApiResponse<List<WorkOrderAttachmentDto>>.Success(result);
        }
        catch (InvalidOperationException ex)
        {
            return ApiResponse<List<WorkOrderAttachmentDto>>.BadRequest(ex.Message);
        }
        catch (Exception ex)
        {
            return ApiResponse<List<WorkOrderAttachmentDto>>.Error(ex.Message);
        }
    }

    /// <summary>
    /// 添加附件
    /// </summary>
    [HttpPost("{id}/attachments")]
    [PermissionAuthorize(Permissions.UPDATE_WORK_ORDERS)]
    public async Task<ActionResult<ApiResponse<WorkOrderAttachmentDto>>> AddAttachment(
        long id,
        [FromBody] AddAttachmentRequest request)
    {
        try
        {
            var result = await _workOrderService.AddAttachmentAsync(id, request.FileName, request.FileUrl, request.FileSize, request.FileType, TenantAppCode, User);
            return ApiResponse<WorkOrderAttachmentDto>.Success(result, "附件添加成功");
        }
        catch (ForbiddenAccessException ex)
        {
            return Ok(ApiResponse<WorkOrderAttachmentDto>.Forbidden(ex.Message));
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
    /// 删除附件
    /// </summary>
    [HttpDelete("attachments/{attachmentId}")]
    [PermissionAuthorize(Permissions.UPDATE_WORK_ORDERS)]
    public async Task<ActionResult<ApiResponse>> DeleteAttachment(long attachmentId)
    {
        try
        {
            await _workOrderService.DeleteAttachmentAsync(attachmentId, TenantAppCode, User);
            return ApiResponse.Success("附件删除成功");
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

}

/// <summary>
/// 分配工单请求
/// </summary>
public class AssignWorkOrderRequest
{
    public string Assignee { get; set; } = string.Empty;
}

/// <summary>
/// 解决工单请求
/// </summary>
public class ResolveWorkOrderRequest
{
    public string ResolveDescription { get; set; } = string.Empty;
}

/// <summary>
/// 拒绝工单请求
/// </summary>
public class RejectWorkOrderRequest
{
    public string Reason { get; set; } = string.Empty;
}

/// <summary>
/// 添加附件请求
/// </summary>
public class AddAttachmentRequest
{
    public string FileName { get; set; } = string.Empty;
    public string FileUrl { get; set; } = string.Empty;
    public string FileSize { get; set; } = string.Empty;
    public string FileType { get; set; } = string.Empty;
}
