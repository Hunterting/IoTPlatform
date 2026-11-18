using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Primitives;

namespace IoTPlatform.Infrastructure;

/// <summary>
/// 包装一个 IFileProvider，使指定首个路径段（不区分大小写）下的文件对静态托管不可见。
/// 用于让 SPA 静态中间件仅服务前端资源，而不暴露 uploads/ 这类只应经鉴权接口访问的目录。
/// </summary>
public sealed class PrefixExcludingFileProvider : IFileProvider
{
    private readonly IFileProvider _inner;
    private readonly string _excludedSegment;

    public PrefixExcludingFileProvider(IFileProvider inner, string excludedSegment)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _excludedSegment = (excludedSegment ?? string.Empty).Trim('/').ToLowerInvariant();
    }

    public IFileInfo GetFileInfo(string subpath)
    {
        if (IsExcluded(subpath))
        {
            return new NotFoundFileInfo(subpath);
        }

        return _inner.GetFileInfo(subpath);
    }

    public IDirectoryContents GetDirectoryContents(string subpath)
    {
        if (IsExcluded(subpath))
        {
            return NotFoundDirectoryContents.Singleton;
        }

        return _inner.GetDirectoryContents(subpath);
    }

    public IChangeToken Watch(string filter) => _inner.Watch(filter);

    private bool IsExcluded(string? subpath)
    {
        if (string.IsNullOrEmpty(_excludedSegment) || string.IsNullOrEmpty(subpath))
        {
            return false;
        }

        var normalized = subpath!.TrimStart('/').ToLowerInvariant();
        return normalized == _excludedSegment
            || normalized.StartsWith(_excludedSegment + "/", StringComparison.Ordinal);
    }
}
