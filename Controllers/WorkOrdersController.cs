using IoTPlatform.Configuration;
using IoTPlatform.DTOs.Requests;
using IoTPlatform.DTOs.Responses;
using IoTPlatform.Filters;
using IoTPlatform.Helpers;
using IoTPlatform.Services;
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

    public WorkOrdersController(IWorkOrderService workOrderService)
    {
        _workOrderService = workOrderService;
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
            var appCode = User.FindFirst("AppCode")?.Value;
            var allowedAreaIds = GetAllowedAreaIds();

            var result = await _workOrderService.GetWorkOrdersAsync(page, pageSize, status, type, priority, appCode, allowedAreaIds);
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
        try
        {
            var appCode = User.FindFirst("AppCode")?.Value;
            var allowedAreaIds = GetAllowedAreaIds();

            var result = await _workOrderService.GetWorkOrderAsync(id, appCode, allowedAreaIds);
            if (result == null)
            {
                return ApiResponse<WorkOrderDto>.NotFound("工单不存在");
            }

            return ApiResponse<WorkOrderDto>.Success(result);
        }
        catch (Exception ex)
        {
            return ApiResponse<WorkOrderDto>.Error(ex.Message);
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
            var result = await _workOrderService.CreateWorkOrderAsync(request);
            return ApiResponse<WorkOrderDto>.Success(result, "工单创建成功");
        }
        catch (InvalidOperationException ex)
        {
            return ApiResponse<WorkOrderDto>.BadRequest(ex.Message);
        }
        catch (Exception ex)
        {
            return ApiResponse<WorkOrderDto>.Error(ex.Message);
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
            var result = await _workOrderService.UpdateWorkOrderAsync(id, request);
            return ApiResponse<WorkOrderDto>.Success(result, "工单更新成功");
        }
        catch (InvalidOperationException ex)
        {
            return ApiResponse<WorkOrderDto>.BadRequest(ex.Message);
        }
        catch (Exception ex)
        {
            return ApiResponse<WorkOrderDto>.Error(ex.Message);
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
            var result = await _workOrderService.AssignWorkOrderAsync(id, request.Assignee);
            return ApiResponse<WorkOrderDto>.Success(result, "工单分配成功");
        }
        catch (InvalidOperationException ex)
        {
            return ApiResponse<WorkOrderDto>.BadRequest(ex.Message);
        }
        catch (Exception ex)
        {
            return ApiResponse<WorkOrderDto>.Error(ex.Message);
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
            var result = await _workOrderService.StartWorkOrderAsync(id);
            return ApiResponse<WorkOrderDto>.Success(result, "工单开始处理");
        }
        catch (InvalidOperationException ex)
        {
            return ApiResponse<WorkOrderDto>.BadRequest(ex.Message);
        }
        catch (Exception ex)
        {
            return ApiResponse<WorkOrderDto>.Error(ex.Message);
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
            var result = await _workOrderService.ResolveWorkOrderAsync(id, request.ResolveDescription);
            return ApiResponse<WorkOrderDto>.Success(result, "工单已解决");
        }
        catch (InvalidOperationException ex)
        {
            return ApiResponse<WorkOrderDto>.BadRequest(ex.Message);
        }
        catch (Exception ex)
        {
            return ApiResponse<WorkOrderDto>.Error(ex.Message);
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
            var result = await _workOrderService.CloseWorkOrderAsync(id);
            return ApiResponse<WorkOrderDto>.Success(result, "工单已关闭");
        }
        catch (InvalidOperationException ex)
        {
            return ApiResponse<WorkOrderDto>.BadRequest(ex.Message);
        }
        catch (Exception ex)
        {
            return ApiResponse<WorkOrderDto>.Error(ex.Message);
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
            var result = await _workOrderService.RejectWorkOrderAsync(id, request.Reason);
            return ApiResponse<WorkOrderDto>.Success(result, "工单已拒绝");
        }
        catch (InvalidOperationException ex)
        {
            return ApiResponse<WorkOrderDto>.BadRequest(ex.Message);
        }
        catch (Exception ex)
        {
            return ApiResponse<WorkOrderDto>.Error(ex.Message);
        }
    }

    /// <summary>
    /// 获取工单日志
    /// </summary>
    [HttpGet("{id}/logs")]
    public async Task<ActionResult<ApiResponse<List<WorkOrderLogDto>>>> GetWorkOrderLogs(long id)
    {
        try
        {
            var result = await _workOrderService.GetWorkOrderLogsAsync(id);
            return ApiResponse<List<WorkOrderLogDto>>.Success(result);
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
        try
        {
            var result = await _workOrderService.GetWorkOrderAttachmentsAsync(id);
            return ApiResponse<List<WorkOrderAttachmentDto>>.Success(result);
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
            var result = await _workOrderService.AddAttachmentAsync(id, request.FileName, request.FileUrl, request.FileSize, request.FileType);
            return ApiResponse<WorkOrderAttachmentDto>.Success(result, "附件添加成功");
        }
        catch (Exception ex)
        {
            return ApiResponse<WorkOrderAttachmentDto>.Error(ex.Message);
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
            await _workOrderService.DeleteAttachmentAsync(attachmentId);
            return ApiResponse.Success("附件删除成功");
        }
        catch (Exception ex)
        {
            return ApiResponse.Error(ex.Message);
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
