using IoTPlatform.Data;
using IoTPlatform.Models;
using IoTPlatform.Services.Guards;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using System.Security.Claims;

namespace IoTPlatform.Services;

/// <summary>
/// 文件存储服务 - 统一处理文件上传、删除、下载。
///
/// <para><b>M9-P0-0</b>：本服务<b>从不读取</b> Request.Headers["X-App-Code"]；appCode 一律由
/// Controller 经 <see cref="ICustomerScopeService"/> 服务端派生后作为参数传入并原样落库。
/// 删除路径新增带 scope 的重载，做到"先查归属 → 校验 → 再删物理文件 + DB 记录"（原则 G）。</para>
/// </summary>
public class FileStorageService : IFileStorageService
{
    private readonly AppDbContext _dbContext;
    private readonly IConfiguration _configuration;
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<FileStorageService> _logger;
    private readonly ICustomerScopeService _customerScope;

    public FileStorageService(
        AppDbContext dbContext,
        IConfiguration configuration,
        IWebHostEnvironment environment,
        ILogger<FileStorageService> logger,
        ICustomerScopeService customerScope)
    {
        _dbContext = dbContext;
        _configuration = configuration;
        _environment = environment;
        _logger = logger;
        _customerScope = customerScope ?? throw new ArgumentNullException(nameof(customerScope));
    }

    /// <summary>
    /// 解析上传根目录：配置为绝对路径时原样使用（便于指向独立数据卷）；
    /// 为相对路径时相对于应用内容根目录（ContentRootPath）解析，保证部署到任意环境都可移植。
    /// </summary>
    private string GetUploadRoot()
    {
        var configured = _configuration["FileStorage:UploadPath"] ?? "uploads";
        return Path.IsPathRooted(configured)
            ? configured
            : Path.Combine(_environment.ContentRootPath, configured);
    }

    /// <summary>
    /// 上传文件并创建附件记录
    /// </summary>
    /// <param name="file">文件</param>
    /// <param name="module">模块名称</param>
    /// <param name="businessId">业务ID</param>
    /// <param name="name">附件名称</param>
    /// <param name="uploadUserId">上传用户ID</param>
    /// <param name="appCode">租户代码</param>
    /// <param name="remark">备注</param>
    /// <returns>创建的附件记录</returns>
    public async Task<Attachment> UploadFileAsync(
        IFormFile file,
        string module,
        long? businessId,
        string name,
        long? uploadUserId,
        string? appCode,
        string? remark = null)
    {
        // 解析上传根目录（绝对路径原样使用；相对路径相对应用根目录解析，跨环境可移植）
        var uploadRoot = GetUploadRoot();
        var maxFileSize = int.Parse(_configuration["FileStorage:MaxFileSizeMB"] ?? "50") * 1024 * 1024;
        var allowedExtensions = _configuration.GetSection("FileStorage:AllowedExtensions").Get<string[]>()
            ?? Array.Empty<string>();

        // 验证文件大小
        if (file.Length > maxFileSize)
        {
            throw new InvalidOperationException($"文件大小超过限制，最大 {maxFileSize / 1024 / 1024}MB");
        }

        // 验证文件扩展名
        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (allowedExtensions.Length > 0 && !allowedExtensions.Contains(extension))
        {
            throw new InvalidOperationException($"不支持的文件类型，允许的类型：{string.Join(", ", allowedExtensions)}");
        }

        // 生成文件存储路径: {uploadPath}/{appCode}/{module}/{date}/{guid}{extension}
        var dateFolder = DateTime.UtcNow.ToString("yyyyMMdd");
        var fileName = $"{Guid.NewGuid()}{extension}";
        
        // 清理 appCode，移除可能导致路径问题的字符
        var invalidChars = Path.GetInvalidPathChars()
            .Concat(new[] { '/', '\\', ':', '*', '?', '"', '<', '>', '|' })
            .ToArray();
        
        var safeAppCode = string.IsNullOrEmpty(appCode) ? "default" : 
            string.Join("_", appCode.Split(invalidChars, StringSplitOptions.RemoveEmptyEntries));
        
        var relativePath = Path.Combine(safeAppCode, module.ToLower(), dateFolder, fileName);
        var fullPath = Path.Combine(uploadRoot, relativePath);

        _logger.LogInformation("准备保存文件: {FullPath}, 上传根目录: {UploadRoot}, 相对路径: {RelativePath}",
            fullPath, uploadRoot, relativePath);

        // 确保目录存在
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            if (!Directory.Exists(directory))
            {
                _logger.LogInformation("创建目录: {Directory}", directory);
                Directory.CreateDirectory(directory);
            }
        }

        // 保存文件
        try
        {
            _logger.LogInformation("开始保存文件到: {FullPath}, 文件大小: {FileSize}", fullPath, file.Length);
            using (var stream = new FileStream(fullPath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }
            _logger.LogInformation("文件保存成功: {FullPath}", fullPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "保存文件失败: {FullPath}", fullPath);
            throw new InvalidOperationException($"保存文件失败: {ex.Message}", ex);
        }

        // 创建附件记录
        var attachment = new Attachment
        {
            Module = module,
            BusinessId = businessId,
            Name = name,
            OriginalName = file.FileName,
            Extension = extension,
            FileUrl = $"/{relativePath.Replace("\\", "/")}",
            FileSize = FormatFileSize(file.Length),
            FileSizeBytes = file.Length,
            ContentType = file.ContentType,
            UploadDate = DateTime.UtcNow,
            UploadUserId = uploadUserId,
            AppCode = appCode,
            Remark = remark
        };

        try
        {
            _logger.LogInformation("准备保存附件记录到数据库: {Name}, Module: {Module}, BusinessId: {BusinessId}", 
                name, module, businessId);
            _dbContext.Attachments.Add(attachment);
            await _dbContext.SaveChangesAsync();
            _logger.LogInformation("附件记录保存成功: ID={Id}, FileUrl={FileUrl}", attachment.Id, attachment.FileUrl);
        }
        catch (Exception dbEx)
        {
            _logger.LogError(dbEx, "保存附件记录到数据库失败: {Name}", name);
            // 删除已上传的文件
            if (System.IO.File.Exists(fullPath))
            {
                try
                {
                    System.IO.File.Delete(fullPath);
                    _logger.LogInformation("已删除已上传的文件: {FullPath}", fullPath);
                }
                catch (Exception deleteEx)
                {
                    _logger.LogWarning(deleteEx, "删除失败的文件时出错: {FullPath}", fullPath);
                }
            }
            throw new InvalidOperationException($"保存附件记录失败: {dbEx.Message}", dbEx);
        }

        _logger.LogInformation(
            "文件上传成功: {Module}/{BusinessId}, 文件名: {FileName}, 大小: {FileSize}",
            module, businessId, file.FileName, attachment.FileSize);

        return attachment;
    }

    /// <summary>
    /// 获取附件列表
    /// </summary>
    public async Task<List<Attachment>> GetAttachmentsAsync(
        string module,
        long? businessId = null,
        int page = 1,
        int pageSize = 100)
    {
        var query = _dbContext.Attachments
            .Where(a => a.Module == module);

        if (businessId.HasValue)
        {
            query = query.Where(a => a.BusinessId == businessId.Value);
        }

        return await query
            .OrderByDescending(a => a.UploadDate)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();
    }

    /// <summary>
    /// 获取附件详情
    /// </summary>
    public async Task<Attachment?> GetAttachmentAsync(long id)
    {
        return await _dbContext.Attachments.FindAsync(id);
    }

    /// <summary>
    /// 旧签名：仅供内部/系统通道使用，委托到带 scope 的重载（null/null = 超管 All 不过滤，原则 E）。
    /// 所有外部 Controller 路径必须使用带 scope 的重载（见 <see cref="DeleteAttachmentAsync(long, string?, IReadOnlyCollection{long})"/>）。
    /// </summary>
    public Task<bool> DeleteAttachmentAsync(long id) => DeleteAttachmentAsync(id, null, null);

    /// <summary>
    /// 带归属校验的附件删除（M9-P0-0）。
    /// 先 IgnoreQueryFilters 点查记录归属 → 按 scope 校验 → 通过后才删物理文件 + DB 记录（原则 G）。
    /// </summary>
    /// <param name="id">附件 ID。</param>
    /// <param name="appCode">服务端派生的 AppCode（Single 分支用；非 header）。</param>
    /// <param name="customerIds">授权客户集合（Multi 分支用；空集合=全拒绝；null=不过滤）。</param>
    /// <exception cref="ForbiddenAccessException">归属校验不通过；此时物理文件与 DB 记录均未改动。</exception>
    public async Task<bool> DeleteAttachmentAsync(
        long id, string? appCode, IReadOnlyCollection<long>? customerIds)
    {
        return await DeleteRecordAsync(id, appCode, customerIds, requireModule: null).ConfigureAwait(false);
    }

    /// <summary>
    /// 带归属校验的合同删除（module=contracts，M9-P0-0）。
    /// 与附件删除共用"先校验再删除"内核，额外约束模块必须为 contracts，避免误删其它附件。
    /// </summary>
    /// <exception cref="ForbiddenAccessException">归属校验不通过；此时物理文件与 DB 记录均未改动。</exception>
    public async Task<bool> DeleteContractAsync(
        long id, string? appCode, IReadOnlyCollection<long>? customerIds)
    {
        return await DeleteRecordAsync(id, appCode, customerIds, Constants.ContractModule).ConfigureAwait(false);
    }

    /// <summary>
    /// 删除内核：查询归属 → 校验 → 删除物理文件 + DB 记录。
    /// </summary>
    private async Task<bool> DeleteRecordAsync(
        long id,
        string? appCode,
        IReadOnlyCollection<long>? customerIds,
        string? requireModule)
    {
        var query = _dbContext.Attachments
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(a => a.Id == id);

        if (requireModule != null)
        {
            query = query.Where(a => a.Module == requireModule);
        }

        var attachment = await query.FirstOrDefaultAsync().ConfigureAwait(false);
        if (attachment == null)
        {
            return false;
        }

        // 【原则 G】校验必须在任何删除动作之前。越权抛出时物理文件与 DB 记录均保持原样。
        await EnsureDeleteAllowedAsync(attachment, appCode, customerIds).ConfigureAwait(false);

        await DeletePhysicalAndRecordAsync(attachment).ConfigureAwait(false);
        _logger.LogInformation("附件记录已删除: {AttachmentId}", id);
        return true;
    }

    /// <summary>
    /// 删除归属校验：
    /// - customerIds 非空：Multi 分支，按 AppCode→CustomerId（1:1 不变）判定集合归属；空集合=全拒绝（fail-closed）。
    /// - customerIds 为 null 且 appCode 非空：Single 分支，单值比对（下级零回归）。
    /// - 两者皆 null：超管 All / 系统内部通道，不过滤（原则 E）。
    /// </summary>
    private async Task EnsureDeleteAllowedAsync(
        Attachment attachment, string? appCode, IReadOnlyCollection<long>? customerIds)
    {
        if (customerIds != null)
        {
            if (string.IsNullOrWhiteSpace(attachment.AppCode))
            {
                throw new ForbiddenAccessException("附件缺少归属，禁止删除");
            }

            var ownerCustomerId = await _customerScope
                .ResolveCustomerIdByAppCodeAsync(attachment.AppCode).ConfigureAwait(false);
            if (!ownerCustomerId.HasValue || !customerIds.Contains(ownerCustomerId.Value))
            {
                throw new ForbiddenAccessException("无权删除该附件");
            }

            return;
        }

        if (!string.IsNullOrEmpty(appCode))
        {
            if (!string.Equals(appCode, attachment.AppCode, StringComparison.Ordinal))
            {
                throw new ForbiddenAccessException("无权删除该附件");
            }

            // appCode 非空即已按单值比对；空 appCode 走下方 both-null 分支（All/内部）由调用方保证。
        }
    }

    /// <summary>删除物理文件 + DB 记录（仅在归属校验通过后调用）。</summary>
    private async Task DeletePhysicalAndRecordAsync(Attachment attachment)
    {
        // 删除物理文件（必须用与上传一致的 UploadRoot 前缀，否则找不到文件）
        if (!string.IsNullOrEmpty(attachment.FileUrl))
        {
            var filePath = Path.Combine(GetUploadRoot(), attachment.FileUrl.TrimStart('/'));
            if (System.IO.File.Exists(filePath))
            {
                System.IO.File.Delete(filePath);
                _logger.LogInformation("物理文件已删除: {FilePath}", filePath);
            }
            else
            {
                _logger.LogWarning("待删除的物理文件不存在: {FilePath}", filePath);
            }
        }

        _dbContext.Attachments.Remove(attachment);
        await _dbContext.SaveChangesAsync().ConfigureAwait(false);
    }

    /// <summary>模块名常量：合同在通用附件表中以 module=contracts 存储。</summary>
    private static class Constants
    {
        public const string ContractModule = "contracts";
    }

    /// <summary>
    /// 获取文件物理路径
    /// </summary>
    public string? GetFilePhysicalPath(Attachment attachment)
    {
        if (string.IsNullOrEmpty(attachment.FileUrl))
        {
            return null;
        }

        var filePath = Path.Combine(GetUploadRoot(), attachment.FileUrl.TrimStart('/'));
        if (!System.IO.File.Exists(filePath))
        {
            return null;
        }

        return Path.GetFullPath(filePath);
    }

    /// <summary>
    /// 根据模块获取ContentType
    /// </summary>
    public static string GetContentType(string extension)
    {
        return extension.ToLower() switch
        {
            ".pdf" => "application/pdf",
            ".doc" => "application/msword",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".xls" => "application/vnd.ms-excel",
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ".ppt" => "application/vnd.ms-powerpoint",
            ".pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".bmp" => "image/bmp",
            ".webp" => "image/webp",
            ".svg" => "image/svg+xml",
            ".txt" => "text/plain",
            ".csv" => "text/csv",
            ".html" => "text/html",
            ".css" => "text/css",
            ".js" => "application/javascript",
            ".json" => "application/json",
            ".xml" => "application/xml",
            ".zip" => "application/zip",
            ".rar" => "application/x-rar-compressed",
            ".7z" => "application/x-7z-compressed",
            ".tar" => "application/x-tar",
            ".gz" => "application/gzip",
            ".mp3" => "audio/mpeg",
            ".wav" => "audio/wav",
            ".mp4" => "video/mp4",
            ".avi" => "video/x-msvideo",
            ".mov" => "video/quicktime",
            _ => "application/octet-stream"
        };
    }

    private static string FormatFileSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB" };
        int order = 0;
        double size = bytes;
        while (size >= 1024 && order < sizes.Length - 1)
        {
            order++;
            size /= 1024;
        }
        return $"{size:0.##} {sizes[order]}";
    }
}
