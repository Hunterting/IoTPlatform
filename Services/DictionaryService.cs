using IoTPlatform.Data.Repositories.Interfaces;
using IoTPlatform.DTOs.Requests;
using IoTPlatform.DTOs.Responses;
using IoTPlatform.Helpers;
using IoTPlatform.Models;
using Microsoft.EntityFrameworkCore;

namespace IoTPlatform.Services;

/// <summary>
/// 字典服务实现（使用仓储模式）
/// </summary>
public class DictionaryService : IDictionaryService
{
    private readonly IDictionaryRepository _dictionaryRepository;
    private readonly IUnitOfWork _unitOfWork;

    public DictionaryService(
        IDictionaryRepository dictionaryRepository,
        IUnitOfWork unitOfWork)
    {
        _dictionaryRepository = dictionaryRepository;
        _unitOfWork = unitOfWork;
    }

    /// <summary>
    /// 获取字典类型列表
    /// </summary>
    public async Task<PagedResponse<DictionaryTypeDto>> GetDictionaryTypesAsync(int page, int pageSize, string? keyword, string? appCode)
    {
        var query = _dictionaryRepository.GetTypesQuery(appCode);

        // 关键词搜索
        if (!string.IsNullOrEmpty(keyword))
        {
            query = query.Where(d =>
                d.Code.Contains(keyword) ||
                d.Name.Contains(keyword));
        }

        var totalCount = await query.CountAsync();

        var types = await query
            .OrderBy(d => d.SortOrder)
            .ThenBy(d => d.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(d => new DictionaryTypeDto
            {
                Id = d.Id,
                Code = d.Code,
                Name = d.Name,
                Description = d.Description,
                SortOrder = d.SortOrder,
                IsActive = d.IsActive,
                AppCode = d.AppCode,
                CreatedAt = d.CreatedAt,
                UpdatedAt = d.UpdatedAt
            })
            .ToListAsync();

        return PagedResponse<DictionaryTypeDto>.Create(types, totalCount, page, pageSize);
    }

    /// <summary>
    /// 获取字典类型详情
    /// </summary>
    public async Task<DictionaryTypeDto?> GetDictionaryTypeAsync(long id, string? appCode)
    {
        var query = _dictionaryRepository.GetTypesQuery(appCode);

        var type = await query.FirstOrDefaultAsync(d => d.Id == id);
        if (type == null) return null;

        return new DictionaryTypeDto
        {
            Id = type.Id,
            Code = type.Code,
            Name = type.Name,
            Description = type.Description,
            SortOrder = type.SortOrder,
            IsActive = type.IsActive,
            AppCode = type.AppCode,
            CreatedAt = type.CreatedAt,
            UpdatedAt = type.UpdatedAt
        };
    }

    /// <summary>
    /// 创建字典类型
    /// </summary>
    public async Task<DictionaryTypeDto> CreateDictionaryTypeAsync(CreateDictionaryTypeRequest request)
    {
        var typeExists = await _dictionaryRepository.TypeCodeExistsAsync(request.Code, request.AppCode);
        if (typeExists)
        {
            throw new InvalidOperationException("字典类型代码已存在");
        }

        var type = new DictionaryTypeConfig
        {
            Code = request.Code,
            Name = request.Name,
            Description = request.Description,
            SortOrder = request.SortOrder,
            IsActive = request.IsActive,
            AppCode = request.AppCode,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _dictionaryRepository.AddTypeAsync(type);
        await _unitOfWork.SaveChangesAsync();

        return new DictionaryTypeDto
        {
            Id = type.Id,
            Code = type.Code,
            Name = type.Name,
            Description = type.Description,
            SortOrder = type.SortOrder,
            IsActive = type.IsActive,
            AppCode = type.AppCode,
            CreatedAt = type.CreatedAt,
            UpdatedAt = type.UpdatedAt
        };
    }

    /// <summary>
    /// 更新字典类型
    /// </summary>
    public async Task<DictionaryTypeDto> UpdateDictionaryTypeAsync(long id, UpdateDictionaryTypeRequest request, string? appCode)
    {
        var type = await _dictionaryRepository.GetTypeByIdAsync(id);
        if (type == null)
        {
            throw new InvalidOperationException("字典类型不存在");
        }

        // 权限检查
        if (!string.IsNullOrEmpty(appCode) && type.AppCode != appCode)
        {
            throw new UnauthorizedAccessException("无权修改该字典类型");
        }

        type.Name = request.Name;
        type.Description = request.Description;
        type.SortOrder = request.SortOrder;
        type.IsActive = request.IsActive;
        type.UpdatedAt = DateTime.UtcNow;

        await _dictionaryRepository.UpdateTypeAsync(type);
        await _unitOfWork.SaveChangesAsync();

        return new DictionaryTypeDto
        {
            Id = type.Id,
            Code = type.Code,
            Name = type.Name,
            Description = type.Description,
            SortOrder = type.SortOrder,
            IsActive = type.IsActive,
            AppCode = type.AppCode,
            CreatedAt = type.CreatedAt,
            UpdatedAt = type.UpdatedAt
        };
    }

    /// <summary>
    /// 删除字典类型
    /// </summary>
    public async Task DeleteDictionaryTypeAsync(long id, string? appCode)
    {
        var type = await _dictionaryRepository.GetTypeByIdAsync(id);
        if (type == null)
        {
            throw new InvalidOperationException("字典类型不存在");
        }

        // 权限检查
        if (!string.IsNullOrEmpty(appCode) && type.AppCode != appCode)
        {
            throw new UnauthorizedAccessException("无权删除该字典类型");
        }

        // 检查是否有字典项
        var hasItems = await _dictionaryRepository.TypeHasItemsAsync(type.Code, type.AppCode);
        if (hasItems)
        {
            throw new InvalidOperationException("字典类型下存在字典项，无法删除");
        }

        await _dictionaryRepository.DeleteTypeAsync(type);
        await _unitOfWork.SaveChangesAsync();
    }

    /// <summary>
    /// 根据类型获取字典项列表
    /// </summary>
    public async Task<List<DictionaryItemDto>> GetDictionaryItemsAsync(string type, string? appCode)
    {
        var query = _dictionaryRepository.GetItemsQuery(appCode, type, activeOnly: true);

        var items = await query
            .OrderBy(d => d.Sort)
            .ThenBy(d => d.Id)
            .Select(d => new DictionaryItemDto
            {
                Id = d.Id,
                Type = d.Type,
                Code = d.Code,
                Name = d.Name,
                Sort = d.Sort,
                Description = d.Description,
                Status = d.Status,
                AppCode = d.AppCode,
                CreatedAt = d.CreatedAt,
                UpdatedAt = d.UpdatedAt
            })
            .ToListAsync();

        return items;
    }

    /// <summary>
    /// 获取字典项详情
    /// </summary>
    public async Task<DictionaryItemDto?> GetDictionaryItemAsync(long id, string? appCode)
    {
        var query = _dictionaryRepository.GetItemsQuery(appCode);

        var item = await query.FirstOrDefaultAsync(d => d.Id == id);
        if (item == null) return null;

        return new DictionaryItemDto
        {
            Id = item.Id,
            Type = item.Type,
            Code = item.Code,
            Name = item.Name,
            Sort = item.Sort,
            Description = item.Description,
            Status = item.Status,
            AppCode = item.AppCode,
            CreatedAt = item.CreatedAt,
            UpdatedAt = item.UpdatedAt
        };
    }

    /// <summary>
    /// 创建字典项
    /// </summary>
    public async Task<DictionaryItemDto> CreateDictionaryItemAsync(CreateDictionaryItemRequest request)
    {
        var typeExists = await _dictionaryRepository.TypeCodeExistsAsync(request.Type, request.AppCode);
        if (!typeExists)
        {
            throw new InvalidOperationException("字典类型不存在");
        }

        var itemExists = await _dictionaryRepository.ItemCodeExistsAsync(request.Type, request.Code, request.AppCode);
        if (itemExists)
        {
            throw new InvalidOperationException("字典项代码已存在");
        }

        var item = new DictionaryItem
        {
            Type = request.Type,
            Code = request.Code,
            Name = request.Name,
            Sort = request.Sort,
            Description = request.Description,
            Status = "active",
            AppCode = request.AppCode,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _dictionaryRepository.AddItemAsync(item);
        await _unitOfWork.SaveChangesAsync();

        return new DictionaryItemDto
        {
            Id = item.Id,
            Type = item.Type,
            Code = item.Code,
            Name = item.Name,
            Sort = item.Sort,
            Description = item.Description,
            Status = item.Status,
            AppCode = item.AppCode,
            CreatedAt = item.CreatedAt,
            UpdatedAt = item.UpdatedAt
        };
    }

    /// <summary>
    /// 更新字典项
    /// </summary>
    public async Task<DictionaryItemDto> UpdateDictionaryItemAsync(long id, UpdateDictionaryItemRequest request, string? appCode)
    {
        var item = await _dictionaryRepository.GetItemByIdAsync(id);
        if (item == null)
        {
            throw new InvalidOperationException("字典项不存在");
        }

        // 权限检查
        if (!string.IsNullOrEmpty(appCode) && item.AppCode != appCode)
        {
            throw new UnauthorizedAccessException("无权修改该字典项");
        }

        item.Name = request.Name;
        item.Sort = request.Sort;
        item.Description = request.Description;
        item.Status = request.Status;
        item.UpdatedAt = DateTime.UtcNow;

        await _dictionaryRepository.UpdateItemAsync(item);
        await _unitOfWork.SaveChangesAsync();

        return new DictionaryItemDto
        {
            Id = item.Id,
            Type = item.Type,
            Code = item.Code,
            Name = item.Name,
            Sort = item.Sort,
            Description = item.Description,
            Status = item.Status,
            AppCode = item.AppCode,
            CreatedAt = item.CreatedAt,
            UpdatedAt = item.UpdatedAt
        };
    }

    /// <summary>
    /// 删除字典项
    /// </summary>
    public async Task DeleteDictionaryItemAsync(long id, string? appCode)
    {
        var item = await _dictionaryRepository.GetItemByIdAsync(id);
        if (item == null)
        {
            throw new InvalidOperationException("字典项不存在");
        }

        // 权限检查
        if (!string.IsNullOrEmpty(appCode) && item.AppCode != appCode)
        {
            throw new UnauthorizedAccessException("无权删除该字典项");
        }

        await _dictionaryRepository.DeleteItemAsync(item);
        await _unitOfWork.SaveChangesAsync();
    }
}
