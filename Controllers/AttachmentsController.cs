using IoTPlatform.Configuration;
using IoTPlatform.Data;
using IoTPlatform.Filters;
using IoTPlatform.Helpers;
using IoTPlatform.Services;
using IoTPlatform.Services.Guards;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace IoTPlatform.Controllers;

/// <summary>
/// 通用附件控制器 - 支持任意功能模块的文件上传和管理
/// </summary>
[ApiController]
[Route("api/v1/attachments")]
[PermissionAuthorize]
public class AttachmentsController : ControllerBase
{
    private readonly AppDbContext _dbContext;
    private readonly IFileStorageService _fileStorageService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AttachmentsController> _logger;
    private readonly ICustomerScopeService _customerScope;

    // 【M9-P0-0】刻意不注入 ITenantContextAccessor：admin 的 tenantContext.AppCode 为 null，
    // 且请求头 X-App-Code 完全前端可控——两者都不得作为归属来源。归属只从服务端 scope 派生。
    public AttachmentsController(
        AppDbContext dbContext,
        IFileStorageService fileStorageService,
        IConfiguration configuration,
        ILogger<AttachmentsController> logger,
        ICustomerScopeService customerScope)
    {
        _dbContext = dbContext;
        _fileStorageService = fileStorageService;
        _configuration = configuration;
        _logger = logger;
        _customerScope = customerScope ?? throw new ArgumentNullException(nameof(customerScope));
    }

    /// <summary>
    /// 上传附件
    /// </summary>
    /// <param name="request">上传请求（multipart/form-data）</param>
    /// <returns>附件信息</returns>
    [HttpPost("upload")]
    [RequestSizeLimit(52_428_800)] // 50MB
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(ApiResponse<AttachmentDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<AttachmentDto>), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<AttachmentDto>>> Upload([FromForm] UploadAttachmentRequest request)
    {
        try
        {
            // 验证参数
            if (request.File == null || request.File.Length == 0)
            {
                return ApiResponse<AttachmentDto>.Error("请选择要上传的文件");
            }

            if (string.IsNullOrWhiteSpace(request.Module))
            {
                return ApiResponse<AttachmentDto>.Error("模块名称不能为空");
            }

            var name = string.IsNullOrWhiteSpace(request.Name)
                ? System.IO.Path.GetFileNameWithoutExtension(request.File.FileName)
                : request.Name;

            // 获取当前用户信息
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var userId = long.TryParse(userIdClaim, out var uid) ? uid : (long?)null;

            // 【M9-P0-0】归属一律服务端派生：绝不读 X-App-Code 头、绝不取 tenantContext 空值。
            var (ok, appCode, errorCode, errorMessage) =
                await ResolveUploadAppCodeAsync(request.CustomerId);
            if (!ok)
            {
                return ApiResponse<AttachmentDto>.Fail(errorCode, errorMessage);
            }

            // 上传文件（appCode 已由服务端派生并校验）
            var attachment = await _fileStorageService.UploadFileAsync(
                request.File,
                request.Module,
                request.BusinessId,
                name,
                userId,
                appCode,
                request.Remark);

            var dto = MapToDto(attachment);
            return ApiResponse<AttachmentDto>.Success(dto, "文件上传成功");
        }
        catch (InvalidOperationException ex)
        {
            return ApiResponse<AttachmentDto>.Error(ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "文件上传失败");
            return ApiResponse<AttachmentDto>.Error("文件上传失败");
        }
    }

    /// <summary>
    /// 批量上传附件
    /// </summary>
    /// <param name="request">批量上传请求（multipart/form-data）</param>
    [HttpPost("upload/batch")]
    [RequestSizeLimit(104_857_600)] // 100MB
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(ApiResponse<List<AttachmentDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<List<AttachmentDto>>), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<List<AttachmentDto>>>> UploadBatch([FromForm] UploadBatchAttachmentRequest request)
    {
        try
        {
            if (request.Files == null || request.Files.Count == 0)
            {
                return ApiResponse<List<AttachmentDto>>.Error("请选择要上传的文件");
            }

            if (string.IsNullOrWhiteSpace(request.Module))
            {
                return ApiResponse<List<AttachmentDto>>.Error("模块名称不能为空");
            }

            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var userId = long.TryParse(userIdClaim, out var uid) ? uid : (long?)null;

            // 【M9-P0-0】每批一次服务端派生 + 校验，批量放大不再放大越权面。
            var (ok, appCode, errorCode, errorMessage) =
                await ResolveUploadAppCodeAsync(request.CustomerId);
            if (!ok)
            {
                return ApiResponse<List<AttachmentDto>>.Fail(errorCode, errorMessage);
            }

            var results = new List<AttachmentDto>();

            foreach (var file in request.Files)
            {
                if (file.Length > 0)
                {
                    var attachment = await _fileStorageService.UploadFileAsync(
                        file,
                        request.Module,
                        request.BusinessId,
                        System.IO.Path.GetFileNameWithoutExtension(file.FileName),
                        userId,
                        appCode,
                        request.Remark);

                    results.Add(MapToDto(attachment));
                }
            }

            return ApiResponse<List<AttachmentDto>>.Success(results, $"成功上传 {results.Count} 个文件");
        }
        catch (InvalidOperationException ex)
        {
            return ApiResponse<List<AttachmentDto>>.Error(ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "批量文件上传失败");
            return ApiResponse<List<AttachmentDto>>.Error("批量文件上传失败");
        }
    }

    /// <summary>
    /// 获取附件列表
    /// </summary>
    /// <param name="module">模块名称</param>
    /// <param name="businessId">业务ID</param>
    /// <param name="page">页码</param>
    /// <param name="pageSize">每页数量</param>
    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResult<AttachmentDto>>>> GetList(
        [FromQuery] string module,
        [FromQuery] long? businessId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 100)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(module))
            {
                return ApiResponse<PagedResult<AttachmentDto>>.Error("模块名称不能为空");
            }

            var query = _dbContext.Attachments.Where(a => a.Module == module);

            if (businessId.HasValue)
            {
                query = query.Where(a => a.BusinessId == businessId.Value);
            }

            // B4/M5：附件同样有 AppCode 归属，admin 的 null appCode 不得退化为全量。
            var listScope = await this.ResolveListScopeAsync(_customerScope);
            query = query.ApplyQueryScope(listScope.AppCode, listScope.CustomerIds, listScope.AppCodes);

            var total = await query.CountAsync();

            var attachments = await query
                .OrderByDescending(a => a.UploadDate)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(a => MapToDto(a))
                .ToListAsync();

            var result = new PagedResult<AttachmentDto>
            {
                Items = attachments,
                TotalCount = total,
                Page = page,
                PageSize = pageSize
            };

            return ApiResponse<PagedResult<AttachmentDto>>.Success(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取附件列表失败");
            return ApiResponse<PagedResult<AttachmentDto>>.Error("获取附件列表失败");
        }
    }

    /// <summary>
    /// 获取附件详情
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<ApiResponse<AttachmentDto>>> GetById(long id)
    {
            // B4/M6：先点查权威归属并按 scope 三分支判定；越权 HTTP 200 + Code=403。
            if (!await this.CanReadDetailAsync<IoTPlatform.Models.Attachment>(id, false))
            {
                return Ok(ApiResponse<AttachmentDto>.Fail(403, "无权访问该记录"));
            }

        try
        {
            var attachment = await _fileStorageService.GetAttachmentAsync(id);
            if (attachment == null)
            {
                return ApiResponse<AttachmentDto>.Error("附件不存在");
            }

            return ApiResponse<AttachmentDto>.Success(MapToDto(attachment));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取附件详情失败: {Id}", id);
            return ApiResponse<AttachmentDto>.Error("获取附件详情失败");
        }
    }

    /// <summary>
    /// 删除附件
    /// </summary>
    [HttpDelete("{id}")]
    [PermissionAuthorize(Permissions.MANAGE_PROJECTS)]
    public async Task<ActionResult<ApiResponse>> Delete(long id)
    {
        try
        {
            // 【M9-P0-0】先解析 scope 并下推归属，Service 内"先查记录归属 → 校验 → 再删物理文件+DB"。
            var scope = await _customerScope.ResolveAsync(User);
            string? appCode = null;
            IReadOnlyCollection<long>? customerIds = null;

            switch (scope.Mode)
            {
                case CustomerScopeMode.Multi:
                    customerIds = scope.CustomerIds; // 空集合=全拒绝，fail-closed（Service 侧 Contains 判定）
                    break;
                case CustomerScopeMode.Single:
                    appCode = User.FindFirst("AppCode")?.Value;
                    if (string.IsNullOrEmpty(appCode))
                    {
                        return Ok(ApiResponse.Forbidden("缺少客户归属"));
                    }
                    break;
                // 超管 All：appCode/customerIds 皆 null = 不过滤（原则 E）。
            }

            var result = await _fileStorageService.DeleteAttachmentAsync(id, appCode, customerIds);
            if (!result)
            {
                return ApiResponse.Error("附件不存在");
            }

            return ApiResponse.Success("删除成功");
        }
        catch (ForbiddenAccessException ex)
        {
            // 越权：物理文件与 DB 记录均未被删除（Service 在校验通过前不做任何写动作）。
            return Ok(ApiResponse.Forbidden(ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "删除附件失败: {Id}", id);
            return ApiResponse.Error("删除附件失败");
        }
    }

    /// <summary>
    /// 下载附件
    /// </summary>
    [HttpGet("{id}/download")]
    public async Task<IActionResult> Download(long id)
    {
        try
        {
            // B4/M6：下载与详情同一归属判据，越权 HTTP 200 + Code=403。
            if (!await this.CanReadDetailAsync<IoTPlatform.Models.Attachment>(id))
            {
                return Ok(ApiResponse.Forbidden("无权下载该附件"));
            }

            var attachment = await _fileStorageService.GetAttachmentAsync(id);
            if (attachment == null)
            {
                return NotFound("附件不存在");
            }

            var filePath = _fileStorageService.GetFilePhysicalPath(attachment);
            if (filePath == null)
            {
                return NotFound("文件不存在");
            }

            var contentType = attachment.ContentType
                ?? FileStorageService.GetContentType(attachment.Extension ?? "");

            return PhysicalFile(
                filePath,
                contentType,
                attachment.OriginalName ?? attachment.Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "下载附件失败: {Id}", id);
            return StatusCode(500, "下载失败");
        }
    }

    /// <summary>
    /// 获取支持的文件类型
    /// </summary>
    [HttpGet("allowed-types")]
    public ActionResult<ApiResponse<object>> GetAllowedTypes()
    {
        var allowedExtensions = _configuration.GetSection("FileStorage:AllowedExtensions")
            .Get<string[]>() ?? Array.Empty<string>();

        var maxFileSizeMB = _configuration["FileStorage:MaxFileSizeMB"] ?? "50";

        return ApiResponse<object>.Success(new
        {
            Extensions = allowedExtensions,
            MaxFileSizeMB = int.Parse(maxFileSizeMB),
            MaxFileSizeBytes = int.Parse(maxFileSizeMB) * 1024 * 1024
        });
    }

    /// <summary>
    /// 【M9-P0-0】服务端派生上传归属 AppCode。
    ///
    /// <para>三分支口径（设计 §2.10 标准模板）：
    /// 超管 All → 必须显式给出目标客户，从 CustomerId 反查 AppCode（不得用 header/隐式 null）；
    /// admin Multi → 必须有 CustomerId 且通过 CanAccessCustomerAsync，再反查 AppCode；
    /// 下级 Single → 只读自身 AppCode claim（逐字零回归），且不读 header。</para>
    ///
    /// <para>返回的 AppCode 是<b>唯一</b>传给 FileStorageService 落库的值；请求头 X-App-Code 不参与任何判定。</para>
    /// </summary>
    /// <param name="requestCustomerId">请求体声明的目标客户 ID（仅作待校验目标，不是授权凭据）。</param>
    /// <returns>(是否放行, 服务端派生 AppCode, 错误码, 错误消息)。</returns>
    private async Task<(bool Ok, string? AppCode, int ErrorCode, string ErrorMessage)> ResolveUploadAppCodeAsync(
        long? requestCustomerId)
    {
        var scope = await _customerScope.ResolveAsync(User);

        switch (scope.Mode)
        {
            case CustomerScopeMode.All:
                if (!requestCustomerId.HasValue)
                {
                    return (false, null, 400, "请指定目标客户");
                }

                var superAppCode = await _customerScope.ResolveAppCodeByCustomerIdAsync(requestCustomerId.Value);
                if (string.IsNullOrEmpty(superAppCode))
                {
                    return (false, null, 400, "目标客户不存在或缺少 AppCode");
                }

                return (true, superAppCode, 200, string.Empty);

            case CustomerScopeMode.Multi:
                if (!requestCustomerId.HasValue ||
                    !await _customerScope.CanAccessCustomerAsync(User, requestCustomerId.Value))
                {
                    return (false, null, 403, "无权在该客户下上传");
                }

                var appCode = await _customerScope.ResolveAppCodeByCustomerIdAsync(requestCustomerId.Value);
                if (string.IsNullOrEmpty(appCode))
                {
                    return (false, null, 400, "目标客户缺少 AppCode");
                }

                return (true, appCode, 200, string.Empty);

            default: // Single：下级 claim 单值零回归，且不读 header。
                var singleAppCode = User.FindFirst("AppCode")?.Value;
                if (string.IsNullOrEmpty(singleAppCode))
                {
                    return (false, null, 403, "缺少客户归属");
                }

                return (true, singleAppCode, 200, string.Empty);
        }
    }

    private static AttachmentDto MapToDto(Models.Attachment attachment)
    {
        return new AttachmentDto
        {
            Id = attachment.Id,
            Module = attachment.Module,
            BusinessId = attachment.BusinessId,
            Name = attachment.Name,
            OriginalName = attachment.OriginalName,
            Extension = attachment.Extension,
            FileUrl = attachment.FileUrl,
            FileSize = attachment.FileSize,
            FileSizeBytes = attachment.FileSizeBytes,
            ContentType = attachment.ContentType,
            UploadDate = attachment.UploadDate,
            UploadUserId = attachment.UploadUserId,
            Remark = attachment.Remark
        };
    }
}

/// <summary>
/// 附件DTO
/// </summary>
public class AttachmentDto
{
    public long Id { get; set; }
    public string Module { get; set; } = string.Empty;
    public long? BusinessId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? OriginalName { get; set; }
    public string? Extension { get; set; }
    public string? FileUrl { get; set; }
    public string? FileSize { get; set; }
    public long FileSizeBytes { get; set; }
    public string? ContentType { get; set; }
    public DateTime UploadDate { get; set; }
    public long? UploadUserId { get; set; }
    public string? Remark { get; set; }
}

/// <summary>
/// 分页结果
/// </summary>
public class PagedResult<T>
{
    public List<T> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}

/// <summary>
/// 单文件上传请求（multipart/form-data）
/// </summary>
public class UploadAttachmentRequest
{
    /// <summary>文件（必填）</summary>
    public IFormFile File { get; set; } = null!;

    /// <summary>模块名称，如 contracts、workorders、archives（必填）</summary>
    public string Module { get; set; } = string.Empty;

    /// <summary>业务ID，如项目ID、工单ID（可选）</summary>
    public long? BusinessId { get; set; }

    /// <summary>
    /// 目标客户 ID（超管/admin 必填，仅作待校验目标，不是授权凭据）。
    /// AppCode 由服务端按 scope 校验后从此 ID 反查，绝不接受请求头/请求体 AppCode 落库。
    /// </summary>
    public long? CustomerId { get; set; }

    /// <summary>附件名称（可选，默认使用文件名）</summary>
    public string? Name { get; set; }

    /// <summary>备注（可选）</summary>
    public string? Remark { get; set; }
}

/// <summary>
/// 批量文件上传请求（multipart/form-data）
/// </summary>
public class UploadBatchAttachmentRequest
{
    /// <summary>文件列表（必填）</summary>
    public List<IFormFile> Files { get; set; } = new();

    /// <summary>模块名称（必填）</summary>
    public string Module { get; set; } = string.Empty;

    /// <summary>业务ID（可选）</summary>
    public long? BusinessId { get; set; }

    /// <summary>目标客户 ID（超管/admin 必填，服务端校验后反查 AppCode，不接受头伪造）。</summary>
    public long? CustomerId { get; set; }

    /// <summary>备注（可选）</summary>
    public string? Remark { get; set; }
}
