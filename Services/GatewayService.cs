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
/// 网关服务实现
/// </summary>
public class GatewayService : IGatewayService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMapper _mapper;

    public GatewayService(IUnitOfWork unitOfWork, IMapper mapper)
    {
        _unitOfWork = unitOfWork;
        _mapper = mapper;
    }

    public async Task<PagedResponse<GatewayDto>> GetGatewaysAsync(int page, int pageSize, string? keyword, string? appCode)
    {
        var repo = _unitOfWork.GetRepository<Gateway>();
        var query = repo.GetQueryable(appCode);

        if (!string.IsNullOrEmpty(keyword))
        {
            query = query.Where(g => g.Name.Contains(keyword) || (g.Description != null && g.Description.Contains(keyword)));
        }

        var totalCount = await query.CountAsync();
        var items = await query
            .OrderByDescending(g => g.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var dtos = _mapper.Map<List<GatewayDto>>(items);

        return new PagedResponse<GatewayDto>
        {
            Items = dtos,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
            TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize)
        };
    }

    public async Task<GatewayDto?> GetGatewayAsync(long id, string? appCode)
    {
        var repo = _unitOfWork.GetRepository<Gateway>();
        var gateway = await repo.GetQueryable(appCode)
            .FirstOrDefaultAsync(g => g.Id == id);

        return gateway == null ? null : _mapper.Map<GatewayDto>(gateway);
    }

    public async Task<GatewayDto> CreateGatewayAsync(CreateGatewayRequest request)
    {
        var gateway = _mapper.Map<Gateway>(request);
        gateway.Status = "offline";
        gateway.Throughput = 0;

        await _unitOfWork.GetRepository<Gateway>().AddAsync(gateway);

        return _mapper.Map<GatewayDto>(gateway);
    }

    public async Task<GatewayDto> UpdateGatewayAsync(long id, UpdateGatewayRequest request, string? appCode)
    {
        var repo = _unitOfWork.GetRepository<Gateway>();
        var gateway = await repo.GetQueryable(appCode)
            .FirstOrDefaultAsync(g => g.Id == id);

        if (gateway == null)
        {
            throw new InvalidOperationException("网关不存在");
        }

        gateway.Name = request.Name;
        if (request.GatewayType != null) gateway.GatewayType = request.GatewayType;
        if (request.SourceProtocol != null) gateway.SourceProtocol = request.SourceProtocol;
        if (request.TargetProtocol != null) gateway.TargetProtocol = request.TargetProtocol;
        if (request.Status != null) gateway.Status = request.Status;
        if (request.IsActive.HasValue) gateway.IsActive = request.IsActive.Value;
        if (request.Config != null) gateway.Config = request.Config;
        if (request.Description != null) gateway.Description = request.Description;
        gateway.UpdatedAt = DateTime.UtcNow;

        await _unitOfWork.SaveChangesAsync();

        return _mapper.Map<GatewayDto>(gateway);
    }

    public async Task DeleteGatewayAsync(long id, string? appCode)
    {
        var repo = _unitOfWork.GetRepository<Gateway>();
        var gateway = await repo.GetQueryable(appCode)
            .FirstOrDefaultAsync(g => g.Id == id);

        if (gateway == null)
        {
            throw new InvalidOperationException("网关不存在");
        }

        await repo.DeleteAsync(gateway);
    }

    public async Task StartGatewayAsync(long id, string? appCode)
    {
        var repo = _unitOfWork.GetRepository<Gateway>();
        var gateway = await repo.GetQueryable(appCode)
            .FirstOrDefaultAsync(g => g.Id == id);

        if (gateway == null)
        {
            throw new InvalidOperationException("网关不存在");
        }

        if (!gateway.IsActive)
        {
            throw new InvalidOperationException("网关未启用，无法启动");
        }

        gateway.Status = "online";
        gateway.UpdatedAt = DateTime.UtcNow;

        await _unitOfWork.SaveChangesAsync();
    }

    public async Task StopGatewayAsync(long id, string? appCode)
    {
        var repo = _unitOfWork.GetRepository<Gateway>();
        var gateway = await repo.GetQueryable(appCode)
            .FirstOrDefaultAsync(g => g.Id == id);

        if (gateway == null)
        {
            throw new InvalidOperationException("网关不存在");
        }

        gateway.Status = "offline";
        gateway.Throughput = 0;
        gateway.UpdatedAt = DateTime.UtcNow;

        await _unitOfWork.SaveChangesAsync();
    }
}
