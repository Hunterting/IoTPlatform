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
/// 合同管理控制器 - 使用通用附件服务
/// </summary>
[ApiController]
[Route("api/v1/contracts")]
[PermissionAuthorize]
public class ContractsController : ControllerBase
{
    private readonly AppDbContext _dbContext;
    private readonly IFileStorageService _fileStorageService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ContractsController> _logger;
    private readonly ICustomerScopeService _customerScope;
    private readonly IDeviceAccessGuard _deviceGuard;

    private const string MODULE_NAME = "contracts";

    // 【M9-P0-0】不注入 ITenantContextAccessor：admin 的 tenantContext.AppCode 为 null，
    // 合同 AppCode 必须从"已授权的项目归属"服务端派生（设计 §2.10）。
    public ContractsController(
        AppDbContext dbContext,
        IFileStorageService fileStorageService,
        IConfiguration configuration,
        ILogger<ContractsController> logger,
        ICustomerScopeService customerScope,
        IDeviceAccessGuard deviceGuard)
    {
        _dbContext = dbContext;
        _fileStorageService = fileStorageService;
        _configuration = configuration;
        _logger = logger;
        _customerScope = customerScope ?? throw new ArgumentNullException(nameof(customerScope));
        _deviceGuard = deviceGuard ?? throw new ArgumentNullException(nameof(deviceGuard));
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
            // 点查项目归属（IgnoreQueryFilters，不依赖启动期冻结的全局过滤器）
            var project = await _dbContext.Projects
                .AsNoTracking()
                .IgnoreQueryFilters()
                .Where(p => p.Id == request.ProjectId)
                .Select(p => new { p.Id, p.CustomerId })
                .FirstOrDefaultAsync();
            if (project == null)
            {
                return ApiResponse<ContractDto>.Error("项目不存在");
            }

            // 【M9-P0-0】在写库前先做归属校验（原则 G）：越权立刻 403，且不落任何文件。
            try
            {
                await _deviceGuard.EnsureProjectAccessAsync(User, request.ProjectId);
            }
            catch (NotFoundException)
            {
                return ApiResponse<ContractDto>.Error("项目不存在");
            }
            catch (ForbiddenAccessException ex)
            {
                return ApiResponse<ContractDto>.Forbidden(ex.Message);
            }

            if (request.File == null || request.File.Length == 0)
            {
                return ApiResponse<ContractDto>.Error("请选择要上传的文件");
            }

            // 获取当前用户信息
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var userId = long.TryParse(userIdClaim, out var uid) ? uid : (long?)null;

            // 【M9-P0-0】AppCode 从项目归属派生：超管/admin 按已校验的 CustomerId 反查；
            // 下级按自身 claim 单值（零回归）。绝不取 tenantContext 空值，也不读任何请求头。
            string? appCode;
            var scope = await _customerScope.ResolveAsync(User);
            if (scope.Mode == CustomerScopeMode.Single)
            {
                appCode = User.FindFirst("AppCode")?.Value;
                if (string.IsNullOrEmpty(appCode))
                {
                    return ApiResponse<ContractDto>.Forbidden("缺少客户归属");
                }
            }
            else
            {
                appCode = await _customerScope.ResolveAppCodeByCustomerIdAsync(project.CustomerId);
                if (string.IsNullOrEmpty(appCode))
                {
                    return ApiResponse<ContractDto>.Error("项目归属客户缺少 AppCode");
                }
            }

            // 使用通用附件服务上传（appCode 为服务端派生值）
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
            // B4/M6：项目归属校验在返回其合同列表之前完成（admin 不得越权枚举）。
            if (!await this.CanReadDetailAsync<IoTPlatform.Models.Project>(projectId))
            {
                return Ok(ApiResponse<List<ContractDto>>.Fail(403, "无权访问该项目的合同"));
            }

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
            // B4/M6：先点查权威归属并按 scope 三分支判定；越权 HTTP 200 + Code=403。
            if (!await this.CanReadDetailAsync<IoTPlatform.Models.Attachment>(id, false))
            {
                return Ok(ApiResponse<ContractDto>.Fail(403, "无权访问该记录"));
            }

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
            // 【M9-P0-0】按 scope 下推归属；Service 先查合同记录归属 → 校验 → 再删物理文件 + DB 记录。
            var scope = await _customerScope.ResolveAsync(User);
            string? appCode = null;
            IReadOnlyCollection<long>? customerIds = null;

            switch (scope.Mode)
            {
                case CustomerScopeMode.Multi:
                    customerIds = scope.CustomerIds; // 空集合=全拒绝，fail-closed
                    break;
                case CustomerScopeMode.Single:
                    appCode = User.FindFirst("AppCode")?.Value;
                    if (string.IsNullOrEmpty(appCode))
                    {
                        return Ok(ApiResponse.Forbidden("缺少客户归属"));
                    }
                    break;
                // 超管 All：两者皆 null = 不过滤（原则 E）。
            }

            var result = await _fileStorageService.DeleteContractAsync(id, appCode, customerIds);
            if (!result)
            {
                return ApiResponse.Error("合同不存在");
            }

            _logger.LogInformation("合同删除成功: {ContractId}", id);
            return ApiResponse.Success("合同删除成功");
        }
        catch (ForbiddenAccessException ex)
        {
            // 越权：物理文件与 DB 记录均未被删除。
            return Ok(ApiResponse.Forbidden(ex.Message));
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
            // B4/M6：合同下载归属校验（与详情同判据）。
            if (!await this.CanReadDetailAsync<IoTPlatform.Models.Attachment>(id))
            {
                return Ok(ApiResponse.Forbidden("无权下载该合同"));
            }

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
