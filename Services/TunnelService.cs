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
/// 隧道服务实现
/// </summary>
public class TunnelService : ITunnelService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMapper _mapper;

    public TunnelService(IUnitOfWork unitOfWork, IMapper mapper)
    {
        _unitOfWork = unitOfWork;
        _mapper = mapper;
    }

    public async Task<PagedResponse<TunnelDto>> GetTunnelsAsync(int page, int pageSize, string? keyword, string? appCode)
    {
        var query = _unitOfWork.GetRepository<Tunnel>().Query(appCode);

        if (!string.IsNullOrEmpty(keyword))
        {
            query = query.Where(t => t.Name.Contains(keyword) || (t.Description != null && t.Description.Contains(keyword)));
        }

        var totalCount = await query.CountAsync();
        var items = await query
            .OrderByDescending(t => t.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var dtos = _mapper.Map<List<TunnelDto>>(items);

        return new PagedResponse<TunnelDto>
        {
            Items = dtos,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
            TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize)
        };
    }

    public async Task<TunnelDto?> GetTunnelAsync(long id, string? appCode)
    {
        var tunnel = await _unitOfWork.GetRepository<Tunnel>().Query(appCode)
            .FirstOrDefaultAsync(t => t.Id == id);

        return tunnel == null ? null : _mapper.Map<TunnelDto>(tunnel);
    }

    public async Task<TunnelDto> CreateTunnelAsync(CreateTunnelRequest request)
    {
        var tunnel = _mapper.Map<Tunnel>(request);
        tunnel.Status = "disconnected";
        tunnel.Bandwidth = "0 Mbps";

        await _unitOfWork.GetRepository<Tunnel>().AddAsync(tunnel);
        await _unitOfWork.SaveChangesAsync();

        return _mapper.Map<TunnelDto>(tunnel);
    }

    public async Task<TunnelDto> UpdateTunnelAsync(long id, UpdateTunnelRequest request, string? appCode)
    {
        var tunnel = await _unitOfWork.GetRepository<Tunnel>().Query(appCode)
            .FirstOrDefaultAsync(t => t.Id == id);

        if (tunnel == null)
        {
            throw new InvalidOperationException("隧道不存在");
        }

        tunnel.Name = request.Name;
        if (request.TunnelType != null) tunnel.TunnelType = request.TunnelType;
        if (request.Status != null) tunnel.Status = request.Status;
        if (request.IsActive.HasValue) tunnel.IsActive = request.IsActive.Value;
        if (request.LocalPort.HasValue) tunnel.LocalPort = request.LocalPort.Value;
        if (request.RemotePort.HasValue) tunnel.RemotePort = request.RemotePort.Value;
        if (request.RemoteHost != null) tunnel.RemoteHost = request.RemoteHost;
        if (request.Encryption.HasValue) tunnel.Encryption = request.Encryption.Value;
        if (request.Config != null) tunnel.Config = request.Config;
        if (request.Description != null) tunnel.Description = request.Description;
        tunnel.UpdatedAt = DateTime.UtcNow;

        await _unitOfWork.SaveChangesAsync();

        return _mapper.Map<TunnelDto>(tunnel);
    }

    public async Task DeleteTunnelAsync(long id, string? appCode)
    {
        var tunnel = await _unitOfWork.GetRepository<Tunnel>().Query(appCode)
            .FirstOrDefaultAsync(t => t.Id == id);

        if (tunnel == null)
        {
            throw new InvalidOperationException("隧道不存在");
        }

        await _unitOfWork.GetRepository<Tunnel>().DeleteAsync(tunnel);
        await _unitOfWork.SaveChangesAsync();
    }

    public async Task ConnectTunnelAsync(long id, string? appCode)
    {
        var tunnel = await _unitOfWork.GetRepository<Tunnel>().Query(appCode)
            .FirstOrDefaultAsync(t => t.Id == id);

        if (tunnel == null)
        {
            throw new InvalidOperationException("隧道不存在");
        }

        if (!tunnel.IsActive)
        {
            throw new InvalidOperationException("隧道未启用，无法连接");
        }

        tunnel.Status = "connected";
        tunnel.Bandwidth = "10 Mbps"; // 模拟带宽
        tunnel.UpdatedAt = DateTime.UtcNow;

        await _unitOfWork.SaveChangesAsync();
    }

    public async Task DisconnectTunnelAsync(long id, string? appCode)
    {
        var tunnel = await _unitOfWork.GetRepository<Tunnel>().Query(appCode)
            .FirstOrDefaultAsync(t => t.Id == id);

        if (tunnel == null)
        {
            throw new InvalidOperationException("隧道不存在");
        }

        tunnel.Status = "disconnected";
        tunnel.Bandwidth = "0 Mbps";
        tunnel.UpdatedAt = DateTime.UtcNow;

        await _unitOfWork.SaveChangesAsync();
    }
}
