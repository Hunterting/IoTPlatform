using IoTPlatform.Models;
using Microsoft.AspNetCore.Http;

namespace IoTPlatform.Services;

/// <summary>
/// 文件存储服务抽象（M9 头伪造修复，设计 §3.2 C）。
///
/// <para><b>安全契约</b>：<c>appCode</c> 必须由调用方（Controller）经
/// <see cref="ICustomerScopeService"/> 服务端派生后传入；本接口<b>没有任何</b>从
/// <c>Request.Headers["X-App-Code"]</c> / 请求体读取归属的入口，也不得新增此类入口。</para>
/// <para>删除重载采用"先查记录归属 → 校验 → 通过后才删物理文件 + DB 记录"（原则 G）。</para>
/// </summary>
public interface IFileStorageService
{
    /// <summary>
    /// 上传文件并创建附件记录。appCode 为服务端派生值，禁止来自客户端可控来源。
    /// 旧签名保留，供内部/下级既有路径零回归调用。
    /// </summary>
    Task<Attachment> UploadFileAsync(
        IFormFile file,
        string module,
        long? businessId,
        string name,
        long? uploadUserId,
        string? appCode,
        string? remark = null);

    /// <summary>获取附件列表。</summary>
    Task<List<Attachment>> GetAttachmentsAsync(
        string module, long? businessId = null, int page = 1, int pageSize = 100);

    /// <summary>获取附件详情。</summary>
    Task<Attachment?> GetAttachmentAsync(long id);

    /// <summary>
    /// 旧签名：仅内部/系统通道使用（调用方须确保已在上层完成归属校验）。
    /// 内部委托到带 scope 的重载并传 null/null（超管 All 语义 = 不过滤）。
    /// </summary>
    Task<bool> DeleteAttachmentAsync(long id);

    /// <summary>
    /// 带归属校验的删除：先按 id 点查记录归属，校验通过后才删物理文件 + DB 记录。
    /// <para>customerIds 非空 → Multi 分支集合判定（空集合=全拒绝）；
    /// appCode 非空且 customerIds 为 null → Single 单值比对；
    /// 两者皆 null → 超管 All / 系统内部通道（不过滤，原则 E）。</para>
    /// </summary>
    /// <exception cref="Guards.ForbiddenAccessException">归属校验不通过（此时物理文件与 DB 记录均未改动）。</exception>
    Task<bool> DeleteAttachmentAsync(long id, string? appCode, IReadOnlyCollection<long>? customerIds);

    /// <summary>
    /// 合同删除（module=contracts）的带归属校验版本，语义同 <see cref="DeleteAttachmentAsync(long, string?, IReadOnlyCollection{long})"/>。
    /// </summary>
    /// <exception cref="Guards.ForbiddenAccessException">归属校验不通过。</exception>
    Task<bool> DeleteContractAsync(long id, string? appCode, IReadOnlyCollection<long>? customerIds);

    /// <summary>获取附件物理路径（供下载）。</summary>
    string? GetFilePhysicalPath(Attachment attachment);
}
