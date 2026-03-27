using IoTPlatform.DTOs.Requests;
using IoTPlatform.DTOs.Responses;

namespace IoTPlatform.Services;

/// <summary>
/// 工单服务接口
/// </summary>
public interface IWorkOrderService
{
    Task<PagedResponse<WorkOrderDto>> GetWorkOrdersAsync(int page, int pageSize, string? status, string? type, string? priority, string? appCode, List<long>? allowedAreaIds);
    Task<WorkOrderDto?> GetWorkOrderAsync(long id, string? appCode, List<long>? allowedAreaIds);
    Task<WorkOrderDto> CreateWorkOrderAsync(CreateWorkOrderRequest request);
    Task<WorkOrderDto> UpdateWorkOrderAsync(long id, UpdateWorkOrderRequest request);
    Task<WorkOrderDto> AssignWorkOrderAsync(long id, string assignee);
    Task<WorkOrderDto> StartWorkOrderAsync(long id);
    Task<WorkOrderDto> ResolveWorkOrderAsync(long id, string resolveDescription);
    Task<WorkOrderDto> CloseWorkOrderAsync(long id);
    Task<WorkOrderDto> RejectWorkOrderAsync(long id, string reason);
    Task<List<WorkOrderLogDto>> GetWorkOrderLogsAsync(long workOrderId);
    Task<List<WorkOrderAttachmentDto>> GetWorkOrderAttachmentsAsync(long workOrderId);
    Task<WorkOrderAttachmentDto> AddAttachmentAsync(long workOrderId, string fileName, string fileUrl, string fileSize, string fileType);
    Task DeleteAttachmentAsync(long attachmentId);
}
