using AutoMapper;
using IoTPlatform.Data;
using IoTPlatform.Data.Repositories.Interfaces;
using IoTPlatform.DTOs.Requests;
using IoTPlatform.DTOs.Responses;
using IoTPlatform.Helpers;
using IoTPlatform.Models;
using Microsoft.EntityFrameworkCore;

namespace IoTPlatform.Services;

/// <summary>
/// 插件服务实现
/// </summary>
public class PluginService : IPluginService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMapper _mapper;

    public PluginService(IUnitOfWork unitOfWork, IMapper mapper)
    {
        _unitOfWork = unitOfWork;
        _mapper = mapper;
    }

    public async Task<PagedResponse<PluginDto>> GetPluginsAsync(int page, int pageSize, string? keyword, string? appCode)
    {
        var query = _unitOfWork.GetRepository<Plugin>().Query(appCode);

        if (!string.IsNullOrEmpty(keyword))
        {
            query = query.Where(p => p.Name.Contains(keyword) || (p.Description != null && p.Description.Contains(keyword)));
        }

        var totalCount = await query.CountAsync();
        var items = await query
            .OrderByDescending(p => p.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var dtos = _mapper.Map<List<PluginDto>>(items);

        return new PagedResponse<PluginDto>
        {
            Items = dtos,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
            TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize)
        };
    }

    public async Task<PluginDto?> GetPluginAsync(long id, string? appCode)
    {
        var plugin = await _unitOfWork.GetRepository<Plugin>().Query(appCode)
            .FirstOrDefaultAsync(p => p.Id == id);

        return plugin == null ? null : _mapper.Map<PluginDto>(plugin);
    }

    public async Task<PluginDto> CreatePluginAsync(CreatePluginRequest request)
    {
        var plugin = _mapper.Map<Plugin>(request);
        plugin.Status = "stopped";
        plugin.InstalledAt = DateTime.UtcNow;

        await _unitOfWork.GetRepository<Plugin>().AddAsync(plugin);
        await _unitOfWork.SaveChangesAsync();

        return _mapper.Map<PluginDto>(plugin);
    }

    public async Task<PluginDto> UpdatePluginAsync(long id, UpdatePluginRequest request, string? appCode)
    {
        var plugin = await _unitOfWork.GetRepository<Plugin>().Query(appCode)
            .FirstOrDefaultAsync(p => p.Id == id);

        if (plugin == null)
        {
            throw new InvalidOperationException("插件不存在");
        }

        plugin.Name = request.Name;
        if (request.Version != null) plugin.Version = request.Version;
        if (request.Status != null) plugin.Status = request.Status;
        if (request.IsActive.HasValue) plugin.IsActive = request.IsActive.Value;
        if (request.PluginType != null) plugin.PluginType = request.PluginType;
        if (request.Description != null) plugin.Description = request.Description;
        if (request.Author != null) plugin.Author = request.Author;
        if (request.FilePath != null) plugin.FilePath = request.FilePath;
        if (request.Config != null) plugin.Config = request.Config;
        if (request.Dependencies != null) plugin.Dependencies = request.Dependencies;
        plugin.UpdatedAt = DateTime.UtcNow;

        await _unitOfWork.SaveChangesAsync();

        return _mapper.Map<PluginDto>(plugin);
    }

    public async Task DeletePluginAsync(long id, string? appCode)
    {
        var plugin = await _unitOfWork.GetRepository<Plugin>().Query(appCode)
            .FirstOrDefaultAsync(p => p.Id == id);

        if (plugin == null)
        {
            throw new InvalidOperationException("插件不存在");
        }

        await _unitOfWork.GetRepository<Plugin>().DeleteAsync(plugin);
        await _unitOfWork.SaveChangesAsync();
    }

    public async Task StartPluginAsync(long id, string? appCode)
    {
        var plugin = await _unitOfWork.GetRepository<Plugin>().Query(appCode)
            .FirstOrDefaultAsync(p => p.Id == id);

        if (plugin == null)
        {
            throw new InvalidOperationException("插件不存在");
        }

        if (!plugin.IsActive)
        {
            throw new InvalidOperationException("插件未启用，无法启动");
        }

        plugin.Status = "running";
        plugin.UpdatedAt = DateTime.UtcNow;

        await _unitOfWork.SaveChangesAsync();
    }

    public async Task StopPluginAsync(long id, string? appCode)
    {
        var plugin = await _unitOfWork.GetRepository<Plugin>().Query(appCode)
            .FirstOrDefaultAsync(p => p.Id == id);

        if (plugin == null)
        {
            throw new InvalidOperationException("插件不存在");
        }

        plugin.Status = "stopped";
        plugin.UpdatedAt = DateTime.UtcNow;

        await _unitOfWork.SaveChangesAsync();
    }
}
