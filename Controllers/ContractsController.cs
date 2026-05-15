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
/// 合同管理控制器 - 使用通用附件服务
/// </summary>
[ApiController]
[Route("api/v1/contracts")]
[PermissionAuthorize]
public class ContractsController : ControllerBase
{
    private readonly AppDbContext _dbContext;
    private readonly FileStorageService _fileStorageService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ContractsController> _logger;
    private readonly ITenantContextAccessor _tenantContextAccessor;

    private const string MODULE_NAME = "contracts";

    public ContractsController(
        AppDbContext dbContext,
        FileStorageService fileStorageService,
        IConfiguration configuration,
        ILogger<ContractsController> logger,
        ITenantContextAccessor tenantContextAccessor)
    {
        _dbContext = dbContext;
        _fileStorageService = fileStorageService;
        _configuration = configuration;
        _logger = logger;
        _tenantContextAccessor = tenantContextAccessor;
    }

    /// <summary>
    /// 上传合同文件（兼容旧接口）
    /// </summary>
    [HttpPost("upload")]
    [RequestSizeLimit(52_428_800)] // 50MB
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(ApiResponse<ContractDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<ContractDto>), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<ContractDto>>> UploadContract([FromForm] UploadContractRequest request)
    {
        try
        {
            // 验证项目存在
            var project = await _dbContext.Projects.FindAsync(request.ProjectId);
            if (project == null)
            {
                return ApiResponse<ContractDto>.Error("项目不存在");
            }

            if (request.File == null || request.File.Length == 0)
            {
                return ApiResponse<ContractDto>.Error("请选择要上传的文件");
            }

            // 获取当前用户信息
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var userId = long.TryParse(userIdClaim, out var uid) ? uid : (long?)null;
            var appCode = _tenantContextAccessor.Current?.AppCode;

            // 使用通用附件服务上传
            var attachment = await _fileStorageService.UploadFileAsync(
                request.File,
                MODULE_NAME,
                request.ProjectId,
                request.Name,
                userId,
                appCode,
                $"合同类型: {request.Type}");

            var dto = new ContractDto
            {
                Id = attachment.Id,
                ProjectId = request.ProjectId,
                Name = attachment.Name,
                Type = request.Type,
                FileUrl = attachment.FileUrl,
                FileSize = attachment.FileSize,
                UploadDate = attachment.UploadDate
            };

            _logger.LogInformation(
                "合同上传成功: {ContractName}, 项目: {ProjectId}, 附件ID: {AttachmentId}",
                request.Name, request.ProjectId, attachment.Id);

            return ApiResponse<ContractDto>.Success(dto, "合同上传成功");
        }
        catch (InvalidOperationException ex)
        {
            return ApiResponse<ContractDto>.Error(ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "合同上传失败");
            return ApiResponse<ContractDto>.Error("合同上传失败");
        }
    }

    /// <summary>
    /// 获取项目的合同列表
    /// </summary>
    [HttpGet("project/{projectId}")]
    public async Task<ActionResult<ApiResponse<List<ContractDto>>>> GetProjectContracts(long projectId)
    {
        try
        {
            // 通过通用附件接口获取
            var attachments = await _fileStorageService.GetAttachmentsAsync(MODULE_NAME, projectId);

            var contracts = attachments.Select(a => new ContractDto
            {
                Id = a.Id,
                ProjectId = a.BusinessId ?? 0,
                Name = a.Name,
                Type = ExtractContractType(a.Remark),
                FileUrl = a.FileUrl,
                FileSize = a.FileSize,
                UploadDate = a.UploadDate
            }).ToList();

            return ApiResponse<List<ContractDto>>.Success(contracts);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取合同列表失败: {ProjectId}", projectId);
            return ApiResponse<List<ContractDto>>.Error(ex.Message);
        }
    }

    /// <summary>
    /// 获取合同详情
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<ApiResponse<ContractDto>>> GetContract(long id)
    {
        try
        {
            var attachment = await _fileStorageService.GetAttachmentAsync(id);
            if (attachment == null)
            {
                return ApiResponse<ContractDto>.Error("合同不存在");
            }

            var dto = new ContractDto
            {
                Id = attachment.Id,
                ProjectId = attachment.BusinessId ?? 0,
                Name = attachment.Name,
                Type = ExtractContractType(attachment.Remark),
                FileUrl = attachment.FileUrl,
                FileSize = attachment.FileSize,
                UploadDate = attachment.UploadDate
            };

            return ApiResponse<ContractDto>.Success(dto);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取合同详情失败: {Id}", id);
            return ApiResponse<ContractDto>.Error(ex.Message);
        }
    }

    /// <summary>
    /// 删除合同
    /// </summary>
    [HttpDelete("{id}")]
    [PermissionAuthorize(Permissions.MANAGE_PROJECTS)]
    public async Task<ActionResult<ApiResponse>> DeleteContract(long id)
    {
        try
        {
            var result = await _fileStorageService.DeleteAttachmentAsync(id);
            if (!result)
            {
                return ApiResponse.Error("合同不存在");
            }

            _logger.LogInformation("合同删除成功: {ContractId}", id);
            return ApiResponse.Success("合同删除成功");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "合同删除失败: {Id}", id);
            return ApiResponse.Error(ex.Message);
        }
    }

    /// <summary>
    /// 下载合同文件
    /// </summary>
    [HttpGet("{id}/download")]
    public async Task<IActionResult> DownloadContract(long id)
    {
        try
        {
            var attachment = await _fileStorageService.GetAttachmentAsync(id);
            if (attachment == null)
            {
                return NotFound("合同不存在");
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
            _logger.LogError(ex, "合同下载失败: {ContractId}", id);
            return StatusCode(500, "下载失败");
        }
    }

    /// <summary>
    /// 从备注中提取合同类型
    /// </summary>
    private static string ExtractContractType(string? remark)
    {
        if (string.IsNullOrEmpty(remark))
        {
            return "service";
        }

        var prefix = "合同类型: ";
        var index = remark.IndexOf(prefix);
        if (index >= 0)
        {
            var type = remark.Substring(index + prefix.Length).Trim();
            var endIndex = type.IndexOf(',');
            if (endIndex > 0)
            {
                type = type.Substring(0, endIndex).Trim();
            }
            return type;
        }

        return "service";
    }
}

/// <summary>
/// 合同上传请求（multipart/form-data）
/// </summary>
public class UploadContractRequest
{
    /// <summary>合同文件（必填）</summary>
    public IFormFile File { get; set; } = null!;

    /// <summary>所属项目ID（必填）</summary>
    public long ProjectId { get; set; }

    /// <summary>合同名称（必填）</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>合同类型（必填）</summary>
    public string Type { get; set; } = string.Empty;
}

/// <summary>
/// 合同DTO
/// </summary>
public class ContractDto
{
    public long Id { get; set; }
    public long ProjectId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string? FileUrl { get; set; }
    public string? FileSize { get; set; }
    public DateTime UploadDate { get; set; }
}
