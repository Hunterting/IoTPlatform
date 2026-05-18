using AutoMapper;
using IoTPlatform.Data;
using IoTPlatform.Data.Repositories.Interfaces;
using IoTPlatform.DTOs.Requests;
using IoTPlatform.DTOs.Responses;
using IoTPlatform.Helpers;
using IoTPlatform.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Data;
using MySqlConnector;

namespace IoTPlatform.Services;

/// <summary>
/// 数据库配置服务实现
/// </summary>
public class DatabaseConfigService : IDatabaseConfigService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMapper _mapper;
    private readonly ILogger<DatabaseConfigService> _logger;

    public DatabaseConfigService(IUnitOfWork unitOfWork, IMapper mapper, ILogger<DatabaseConfigService> logger)
    {
        _unitOfWork = unitOfWork;
        _mapper = mapper;
        _logger = logger;
    }

    public async Task<PagedResponse<DatabaseConfigDto>> GetDatabaseConfigsAsync(int page, int pageSize, string? keyword, string? databaseType, string? appCode)
    {
        var repository = _unitOfWork.GetRepository<DatabaseConfig>();
        var query = repository.Query(appCode);

        if (!string.IsNullOrEmpty(keyword))
        {
            query = query.Where(d => d.Name.Contains(keyword) || (d.Description != null && d.Description.Contains(keyword)));
        }

        if (!string.IsNullOrEmpty(databaseType))
        {
            query = query.Where(d => d.DatabaseType == databaseType);
        }

        var totalCount = await query.CountAsync();
        var items = await query
            .OrderByDescending(d => d.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var dtos = _mapper.Map<List<DatabaseConfigDto>>(items);

        return new PagedResponse<DatabaseConfigDto>
        {
            Items = dtos,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
            TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize)
        };
    }

    public async Task<DatabaseConfigDto?> GetDatabaseConfigAsync(long id, string? appCode)
    {
        var config = await _unitOfWork.GetRepository<DatabaseConfig>()
            .Query(appCode)
            .FirstOrDefaultAsync(d => d.Id == id);

        return config == null ? null : _mapper.Map<DatabaseConfigDto>(config);
    }

    public async Task<DatabaseConfigDto> CreateDatabaseConfigAsync(CreateDatabaseConfigRequest request)
    {
        var config = _mapper.Map<DatabaseConfig>(request);
        config.Status = "disconnected";

        // 加密密码
        if (!string.IsNullOrEmpty(request.Password))
        {
            config.EncryptedPassword = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(request.Password));
        }

        await _unitOfWork.GetRepository<DatabaseConfig>().AddAsync(config);
        await _unitOfWork.SaveChangesAsync();

        return _mapper.Map<DatabaseConfigDto>(config);
    }

    public async Task<DatabaseConfigDto> UpdateDatabaseConfigAsync(long id, UpdateDatabaseConfigRequest request, string? appCode)
    {
        var config = await _unitOfWork.GetRepository<DatabaseConfig>()
            .Query(appCode)
            .FirstOrDefaultAsync(d => d.Id == id);

        if (config == null)
        {
            throw new InvalidOperationException("数据库配置不存在");
        }

        config.Name = request.Name;
        if (request.DatabaseType != null) config.DatabaseType = request.DatabaseType;
        if (request.Status != null) config.Status = request.Status;
        if (request.IsActive.HasValue) config.IsActive = request.IsActive.Value;
        if (request.Host != null) config.Host = request.Host;
        if (request.Port.HasValue) config.Port = request.Port.Value;
        if (request.DatabaseName != null) config.DatabaseName = request.DatabaseName;
        if (request.Username != null) config.Username = request.Username;
        if (request.Password != null)
        {
            config.EncryptedPassword = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(request.Password));
        }
        if (request.ConnectionString != null) config.ConnectionString = request.ConnectionString;
        if (request.Config != null) config.Config = request.Config;
        if (request.Description != null) config.Description = request.Description;
        config.UpdatedAt = DateTime.UtcNow;

        await _unitOfWork.SaveChangesAsync();

        return _mapper.Map<DatabaseConfigDto>(config);
    }

    public async Task DeleteDatabaseConfigAsync(long id, string? appCode)
    {
        var config = await _unitOfWork.GetRepository<DatabaseConfig>()
            .Query(appCode)
            .FirstOrDefaultAsync(d => d.Id == id);

        if (config == null)
        {
            throw new InvalidOperationException("数据库配置不存在");
        }

        await _unitOfWork.GetRepository<DatabaseConfig>().DeleteAsync(config);
        await _unitOfWork.SaveChangesAsync();
    }

    public async Task<bool> TestConnectionAsync(TestDatabaseConnectionRequest request)
    {
        try
        {
            // 根据数据库类型构建连接字符串并测试
            string connectionString = BuildConnectionString(request);

            return await Task.Run(() =>
            {
                using var conn = CreateConnection(request.DatabaseType, connectionString);
                conn.Open();
                return true;
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "测试数据库连接失败");
            return false;
        }
    }

    public async Task<bool> TestConnectionByIdAsync(long id, string? appCode)
    {
        var config = await _unitOfWork.GetRepository<DatabaseConfig>()
            .Query(appCode)
            .FirstOrDefaultAsync(d => d.Id == id);

        if (config == null)
        {
            throw new InvalidOperationException("数据库配置不存在");
        }

        var request = new TestDatabaseConnectionRequest
        {
            DatabaseType = config.DatabaseType,
            Host = config.Host,
            Port = config.Port,
            DatabaseName = config.DatabaseName,
            Username = config.Username,
            ConnectionString = config.ConnectionString
        };

        if (!string.IsNullOrEmpty(config.EncryptedPassword))
        {
            request.Password = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(config.EncryptedPassword));
        }

        var result = await TestConnectionAsync(request);

        config.Status = result ? "connected" : "disconnected";
        config.LastTestAt = DateTime.UtcNow;
        await _unitOfWork.SaveChangesAsync();

        return result;
    }

    private string BuildConnectionString(TestDatabaseConnectionRequest request)
    {
        if (!string.IsNullOrEmpty(request.ConnectionString))
        {
            return request.ConnectionString;
        }

        return request.DatabaseType switch
        {
            "MySQL" => $"Server={request.Host};Port={request.Port};Database={request.DatabaseName};Uid={request.Username};Pwd={request.Password};",
            "PostgreSQL" => $"Host={request.Host};Port={request.Port};Database={request.DatabaseName};Username={request.Username};Password={request.Password};",
            "SQLServer" => $"Server={request.Host},{request.Port};Database={request.DatabaseName};User Id={request.Username};Password={request.Password};",
            _ => throw new NotSupportedException($"不支持的数据库类型: {request.DatabaseType}")
        };
    }

    private IDbConnection CreateConnection(string databaseType, string connectionString)
    {
        return databaseType switch
        {
            "MySQL" => new MySqlConnection(connectionString),
            "PostgreSQL" => new Npgsql.NpgsqlConnection(connectionString),
            "SQLServer" => new Microsoft.Data.SqlClient.SqlConnection(connectionString),
            _ => throw new NotSupportedException($"不支持的数据库类型: {databaseType}")
        };
    }
}
