using IoTPlatform.Data;
using IoTPlatform.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using System.Security.Claims;

namespace IoTPlatform.Services;

/// <summary>
/// 文件存储服务 - 统一处理文件上传、删除、下载
/// </summary>
public class FileStorageService
{
    private readonly AppDbContext _dbContext;
    private readonly IConfiguration _configuration;
    private readonly ILogger<FileStorageService> _logger;

    public FileStorageService(
        AppDbContext dbContext,
        IConfiguration configuration,
        ILogger<FileStorageService> logger)
    {
        _dbContext = dbContext;
        _configuration = configuration;
        _logger = logger;
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
        // 获取配置
        var uploadPath = _configuration["FileStorage:UploadPath"] ?? "uploads";
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
        var fullPath = Path.Combine(uploadPath, relativePath);
        
        _logger.LogInformation("准备保存文件: {FullPath}, 上传路径: {UploadPath}, 相对路径: {RelativePath}", 
            fullPath, uploadPath, relativePath);

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
    /// 删除附件（同时删除物理文件）
    /// </summary>
    public async Task<bool> DeleteAttachmentAsync(long id)
    {
        var attachment = await _dbContext.Attachments.FindAsync(id);
        if (attachment == null)
        {
            return false;
        }

        // 删除物理文件
        if (!string.IsNullOrEmpty(attachment.FileUrl))
        {
            var filePath = attachment.FileUrl.TrimStart('/');
            if (System.IO.File.Exists(filePath))
            {
                System.IO.File.Delete(filePath);
                _logger.LogInformation("物理文件已删除: {FilePath}", filePath);
            }
        }

        _dbContext.Attachments.Remove(attachment);
        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("附件记录已删除: {AttachmentId}", id);
        return true;
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

        var filePath = attachment.FileUrl.TrimStart('/');
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
