using IoTPlatform.DTOs.Requests;
using IoTPlatform.DTOs.Responses;
using IoTPlatform.Helpers;

namespace IoTPlatform.Services;

/// <summary>
/// 工单服务接口
/// </summary>
public interface IWorkOrderService
{
    Task<PagedResponse<WorkOrderDto>> GetWorkOrdersAsync(int page, int pageSize, string? status, string? type, string? priority, string? appCode, List<long>? allowedAreaIds);
    Task<WorkOrderDto?> GetWorkOrderAsync(long id, string? appCode, List<long>? allowedAreaIds);
    /// <param name="appCode">租户 AppCode（服务端从 JWT 解析，避免前端越权伪造）</param>
    Task<WorkOrderDto> CreateWorkOrderAsync(CreateWorkOrderRequest request, string? appCode = null);
    Task<WorkOrderDto> UpdateWorkOrderAsync(long id, UpdateWorkOrderRequest request, string? appCode = null);
    Task<WorkOrderDto> AssignWorkOrderAsync(long id, string assignee, string? appCode = null);
    Task<WorkOrderDto> StartWorkOrderAsync(long id, string? appCode = null);
    Task<WorkOrderDto> ResolveWorkOrderAsync(long id, string resolveDescription, string? appCode = null);
    Task<WorkOrderDto> CloseWorkOrderAsync(long id, string? appCode = null);
    Task<WorkOrderDto> RejectWorkOrderAsync(long id, string reason, string? appCode = null);
    Task<List<WorkOrderLogDto>> GetWorkOrderLogsAsync(long workOrderId, string? appCode = null);
    Task<List<WorkOrderAttachmentDto>> GetWorkOrderAttachmentsAsync(long workOrderId, string? appCode = null);
    Task<WorkOrderAttachmentDto> AddAttachmentAsync(long workOrderId, string fileName, string fileUrl, string fileSize, string fileType, string? appCode = null);
    Task DeleteAttachmentAsync(long attachmentId, string? appCode = null);
}
