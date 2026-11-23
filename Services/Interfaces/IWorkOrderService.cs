using System.Security.Claims;
using IoTPlatform.DTOs.Requests;
using IoTPlatform.DTOs.Responses;
using IoTPlatform.Helpers;

namespace IoTPlatform.Services;

/// <summary>
/// 工单服务接口
/// </summary>
public interface IWorkOrderService
{
    /// <summary>旧签名保留：内部/下级路径零回归。</summary>
    Task<PagedResponse<WorkOrderDto>> GetWorkOrdersAsync(int page, int pageSize, string? status, string? type, string? priority, string? appCode, List<long>? allowedAreaIds);

    /// <summary>B4/M5：Multi 授权客户集合范围下推。</summary>
    Task<PagedResponse<WorkOrderDto>> GetWorkOrdersAsync(int page, int pageSize, string? status, string? type, string? priority, string? appCode, List<long>? allowedAreaIds, IReadOnlyCollection<long>? customerIds, IReadOnlyCollection<string>? appCodes = null) => GetWorkOrdersAsync(page, pageSize, status, type, priority, appCode, allowedAreaIds);
    Task<WorkOrderDto?> GetWorkOrderAsync(long id, string? appCode, List<long>? allowedAreaIds);
    /// <param name="appCode">租户 AppCode（服务端从 JWT 解析，避免前端越权伪造）</param>
    Task<WorkOrderDto> CreateWorkOrderAsync(CreateWorkOrderRequest request, string? appCode = null);

    /// <summary>
    /// M1 二次防御重载：仅当 Controller 已通过 ICustomerScopeService 校验目标客户时传入
    /// <c>customerScopeVerified: true</c>；否则一律拒绝入库。
    /// </summary>
    Task<WorkOrderDto> CreateWorkOrderAsync(
        CreateWorkOrderRequest request,
        string? appCode,
        bool customerScopeVerified);

    Task<WorkOrderDto> UpdateWorkOrderAsync(long id, UpdateWorkOrderRequest request, string? appCode = null);
    Task<WorkOrderDto> AssignWorkOrderAsync(long id, string assignee, string? appCode = null);
    Task<WorkOrderDto> StartWorkOrderAsync(long id, string? appCode = null);
    Task<WorkOrderDto> ResolveWorkOrderAsync(long id, string resolveDescription, string? appCode = null);
    Task<WorkOrderDto> CloseWorkOrderAsync(long id, string? appCode = null);
    Task<WorkOrderDto> RejectWorkOrderAsync(long id, string reason, string? appCode = null);

    // ── M3 显式 principal 重载：guard 在写库之前，旧签名保留供内部通道零回归 ──────────
    Task<WorkOrderDto> UpdateWorkOrderAsync(long id, UpdateWorkOrderRequest request, string? appCode, ClaimsPrincipal principal);
    Task<WorkOrderDto> AssignWorkOrderAsync(long id, string assignee, string? appCode, ClaimsPrincipal principal);
    Task<WorkOrderDto> StartWorkOrderAsync(long id, string? appCode, ClaimsPrincipal principal);
    Task<WorkOrderDto> ResolveWorkOrderAsync(long id, string resolveDescription, string? appCode, ClaimsPrincipal principal);
    Task<WorkOrderDto> CloseWorkOrderAsync(long id, string? appCode, ClaimsPrincipal principal);
    Task<WorkOrderDto> RejectWorkOrderAsync(long id, string reason, string? appCode, ClaimsPrincipal principal);

    Task<List<WorkOrderLogDto>> GetWorkOrderLogsAsync(long workOrderId, string? appCode = null);
    Task<List<WorkOrderAttachmentDto>> GetWorkOrderAttachmentsAsync(long workOrderId, string? appCode = null);
    Task<WorkOrderAttachmentDto> AddAttachmentAsync(long workOrderId, string fileName, string fileUrl, string fileSize, string fileType, string? appCode = null);
    Task DeleteAttachmentAsync(long attachmentId, string? appCode = null);
    Task<WorkOrderAttachmentDto> AddAttachmentAsync(long workOrderId, string fileName, string fileUrl, string fileSize, string fileType, string? appCode, ClaimsPrincipal principal);
    Task DeleteAttachmentAsync(long attachmentId, string? appCode, ClaimsPrincipal principal);
}
