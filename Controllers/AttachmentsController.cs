using IoTPlatform.Configuration;
using IoTPlatform.Data;
using IoTPlatform.Filters;
using IoTPlatform.Helpers;
using IoTPlatform.Infrastructure.Tenant;
using IoTPlatform.Services;
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
    private readonly FileStorageService _fileStorageService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AttachmentsController> _logger;
    private readonly ITenantContextAccessor _tenantContextAccessor;

    public AttachmentsController(
        AppDbContext dbContext,
        FileStorageService fileStorageService,
        IConfiguration configuration,
        ILogger<AttachmentsController> logger,
        ITenantContextAccessor tenantContextAccessor)
    {
        _dbContext = dbContext;
        _fileStorageService = fileStorageService;
        _configuration = configuration;
        _logger = logger;
        _tenantContextAccessor = tenantContextAccessor;
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

            // 获取当前用户和租户信息
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var userId = long.TryParse(userIdClaim, out var uid) ? uid : (long?)null;
            var appCode = _tenantContextAccessor.Current?.AppCode;

            // 上传文件
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
            var appCode = _tenantContextAccessor.Current?.AppCode;

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
            var result = await _fileStorageService.DeleteAttachmentAsync(id);
            if (!result)
            {
                return ApiResponse.Error("附件不存在");
            }

            return ApiResponse.Success("删除成功");
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

    /// <summary>备注（可选）</summary>
    public string? Remark { get; set; }
}
