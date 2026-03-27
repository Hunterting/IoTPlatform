using StackExchange.Redis;

namespace IoTPlatform.Infrastructure.Cache;

/// <summary>
/// Redis缓存服务
/// </summary>
public interface IRedisCacheService
{
    /// <summary>
    /// 设置缓存
    /// </summary>
    Task SetAsync<T>(string key, T value, TimeSpan? expiry = null);

    /// <summary>
    /// 获取缓存
    /// </summary>
    Task<T?> GetAsync<T>(string key);

    /// <summary>
    /// 删除缓存
    /// </summary>
    Task DeleteAsync(string key);

    /// <summary>
    /// 检查缓存是否存在
    /// </summary>
    Task<bool> ExistsAsync(string key);

    /// <summary>
    /// 设置缓存（带过期时间）
    /// </summary>
    Task SetWithExpiryAsync<T>(string key, T value, TimeSpan expiry);

    /// <summary>
    /// 批量删除缓存
    /// </summary>
    Task DeleteMultipleAsync(IEnumerable<string> keys);

    /// <summary>
    /// 清空所有缓存
    /// </summary>
    Task FlushAllAsync();
}

/// <summary>
/// Redis缓存服务实现
/// </summary>
public class RedisCacheService : IRedisCacheService, IDisposable
{
    private readonly IConnectionMultiplexer _redis;
    private readonly IDatabase _database;
    private readonly IConfiguration _configuration;
    private readonly bool _enabled;
    private readonly ILogger<RedisCacheService> _logger;

    public RedisCacheService(IConfiguration configuration, ILogger<RedisCacheService> logger)
    {
        _configuration = configuration;
        _logger = logger;
        _enabled = configuration.GetValue<bool>("Redis:Enabled", false);

        if (_enabled)
        {
            var connectionString = configuration["Redis:ConnectionString"];
            if (!string.IsNullOrEmpty(connectionString))
            {
                try
                {
                    _redis = ConnectionMultiplexer.Connect(connectionString);
                    _database = _redis.GetDatabase();
                    _logger.LogInformation("Redis连接成功");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Redis连接失败，将禁用缓存");
                    _enabled = false;
                    _redis = null!;
                    _database = null!;
                }
            }
            else
            {
                _logger.LogWarning("Redis连接字符串未配置，将禁用缓存");
                _enabled = false;
                _redis = null!;
                _database = null!;
            }
        }
        else
        {
            _logger.LogInformation("Redis已禁用");
            _redis = null!;
            _database = null!;
        }
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan? expiry = null)
    {
        if (!_enabled || _database == null) return;

        try
        {
            var serialized = System.Text.Json.JsonSerializer.Serialize(value);
            await _database.StringSetAsync(key, serialized, expiry);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "设置Redis缓存失败: {Key}", key);
        }
    }

    public async Task SetWithExpiryAsync<T>(string key, T value, TimeSpan expiry)
    {
        if (!_enabled || _database == null) return;

        try
        {
            var serialized = System.Text.Json.JsonSerializer.Serialize(value);
            await _database.StringSetAsync(key, serialized, expiry);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "设置Redis缓存失败: {Key}", key);
        }
    }

    public async Task<T?> GetAsync<T>(string key)
    {
        if (!_enabled || _database == null) return default;

        try
        {
            var value = await _database.StringGetAsync(key);
            if (value.IsNullOrEmpty) return default;

            return System.Text.Json.JsonSerializer.Deserialize<T>(value!);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取Redis缓存失败: {Key}", key);
            return default;
        }
    }

    public async Task DeleteAsync(string key)
    {
        if (!_enabled || _database == null) return;

        try
        {
            await _database.KeyDeleteAsync(key);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "删除Redis缓存失败: {Key}", key);
        }
    }

    public async Task<bool> ExistsAsync(string key)
    {
        if (!_enabled || _database == null) return false;

        try
        {
            return await _database.KeyExistsAsync(key);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "检查Redis缓存存在失败: {Key}", key);
            return false;
        }
    }

    public async Task DeleteMultipleAsync(IEnumerable<string> keys)
    {
        if (!_enabled || _database == null) return;

        try
        {
            var redisKeys = keys.Select(k => (RedisKey)k).ToArray();
            await _database.KeyDeleteAsync(redisKeys);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "批量删除Redis缓存失败");
        }
    }

    public async Task FlushAllAsync()
    {
        if (!_enabled || _redis == null) return;

        try
        {
            var endpoints = _redis.GetEndPoints();
            foreach (var endpoint in endpoints)
            {
                var server = _redis.GetServer(endpoint);
                await server.FlushAllDatabasesAsync();
            }
            _logger.LogInformation("清空所有Redis缓存成功");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "清空Redis缓存失败");
        }
    }

    public void Dispose()
    {
        _redis?.Dispose();
    }
}
