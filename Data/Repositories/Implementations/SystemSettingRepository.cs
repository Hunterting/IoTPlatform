using IoTPlatform.Data;
using IoTPlatform.Data.Repositories.Interfaces;
using IoTPlatform.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace IoTPlatform.Data.Repositories.Implementations
{
    public class SystemSettingRepository : Repository<SystemSetting>, ISystemSettingRepository
    {
        public SystemSettingRepository(AppDbContext context) : base(context)
        {
        }

        public async Task<SystemSetting?> GetByKeyAsync(string key, string? appCode = null)
        {
            var query = _dbSet.Where(s => s.SettingKey == key);

            if (!string.IsNullOrEmpty(appCode))
            {
                query = query.Where(s => s.AppCode == appCode);
            }

            return await query.FirstOrDefaultAsync();
        }

        public async Task<IEnumerable<SystemSetting>> GetByCategoryAsync(string category, string? appCode = null)
        {
            var query = _dbSet.Where(s => s.Category == category);

            if (!string.IsNullOrEmpty(appCode))
            {
                query = query.Where(s => s.AppCode == appCode);
            }

            return await query
                .OrderBy(s => s.SortOrder)
                .ThenBy(s => s.Id)
                .ToListAsync();
        }

        public async Task<bool> KeyExistsAsync(string key, string? appCode = null, long? excludeSettingId = null)
        {
            var query = _dbSet.Where(s => s.SettingKey == key);

            if (!string.IsNullOrEmpty(appCode))
            {
                query = query.Where(s => s.AppCode == appCode);
            }

            if (excludeSettingId.HasValue)
            {
                query = query.Where(s => s.Id != excludeSettingId.Value);
            }

            return await query.AnyAsync();
        }

        public async Task<SystemSetting> GetOrCreateAsync(string key, string? defaultValue = null, string? description = null,
            string? category = null, string? valueType = "string", string? appCode = null)
        {
            var setting = await GetByKeyAsync(key, appCode);

            if (setting == null)
            {
                setting = new SystemSetting
                {
                    SettingKey = key,
                    SettingValue = defaultValue ?? string.Empty,
                    Description = description,
                    Category = category,
                    ValueType = valueType,
                    AppCode = appCode,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                _dbSet.Add(setting);
                await _context.SaveChangesAsync();
            }

            return setting;
        }

        public async Task<T?> GetValueAsync<T>(string key, T? defaultValue = default, string? appCode = null)
        {
            var setting = await GetByKeyAsync(key, appCode);

            if (setting == null || string.IsNullOrEmpty(setting.SettingValue))
            {
                return defaultValue;
            }

            try
            {
                if (typeof(T) == typeof(string))
                {
                    return (T)(object)setting.SettingValue;
                }
                else if (typeof(T) == typeof(int))
                {
                    if (int.TryParse(setting.SettingValue, out int intValue))
                    {
                        return (T)(object)intValue;
                    }
                }
                else if (typeof(T) == typeof(bool))
                {
                    if (bool.TryParse(setting.SettingValue, out bool boolValue))
                    {
                        return (T)(object)boolValue;
                    }
                }
                else if (typeof(T) == typeof(double))
                {
                    if (double.TryParse(setting.SettingValue, out double doubleValue))
                    {
                        return (T)(object)doubleValue;
                    }
                }
                else if (typeof(T) == typeof(DateTime))
                {
                    if (DateTime.TryParse(setting.SettingValue, out DateTime dateTimeValue))
                    {
                        return (T)(object)dateTimeValue;
                    }
                }
                else
                {
                    // 尝试JSON反序列化
                    return JsonSerializer.Deserialize<T>(setting.SettingValue);
                }
            }
            catch
            {
                // 转换失败，返回默认值
            }

            return defaultValue;
        }

        public async Task SetValueAsync<T>(string key, T value, string? description = null, string? category = null, string? appCode = null)
        {
            var setting = await GetByKeyAsync(key, appCode);

            string valueString;
            if (typeof(T) == typeof(string))
            {
                valueString = (string)(object)value;
            }
            else if (value is DateTime dt)
            {
                valueString = dt.ToString("o");
            }
            else
            {
                valueString = JsonSerializer.Serialize(value);
            }

            if (setting == null)
            {
                setting = new SystemSetting
                {
                    SettingKey = key,
                    SettingValue = valueString,
                    Description = description,
                    Category = category,
                    ValueType = typeof(T).Name.ToLower(),
                    AppCode = appCode,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                _dbSet.Add(setting);
            }
            else
            {
                setting.SettingValue = valueString;
                if (!string.IsNullOrEmpty(description))
                {
                    setting.Description = description;
                }
                if (!string.IsNullOrEmpty(category))
                {
                    setting.Category = category;
                }
                setting.UpdatedAt = DateTime.UtcNow;
            }

            await _context.SaveChangesAsync();
        }

        public async Task<Dictionary<string, string?>> GetValuesAsync(IEnumerable<string> keys, string? appCode = null)
        {
            var query = _dbSet.Where(s => keys.Contains(s.SettingKey));

            if (!string.IsNullOrEmpty(appCode))
            {
                query = query.Where(s => s.AppCode == appCode);
            }

            var settings = await query
                .Select(s => new { s.SettingKey, s.SettingValue })
                .ToListAsync();

            var result = new Dictionary<string, string?>();
            foreach (var key in keys)
            {
                var setting = settings.FirstOrDefault(s => s.SettingKey == key);
                result[key] = setting?.SettingValue;
            }

            return result;
        }

        public async Task SetValuesAsync(Dictionary<string, string> values, string? appCode = null)
        {
            var existingSettings = await GetValuesAsync(values.Keys, appCode);

            foreach (var kvp in values)
            {
                var setting = existingSettings.ContainsKey(kvp.Key)
                    ? await GetByKeyAsync(kvp.Key, appCode)
                    : null;

                if (setting == null)
                {
                    setting = new SystemSetting
                    {
                        SettingKey = kvp.Key,
                        SettingValue = kvp.Value,
                        AppCode = appCode,
                        IsActive = true,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    };
                    _dbSet.Add(setting);
                }
                else
                {
                    setting.SettingValue = kvp.Value;
                    setting.UpdatedAt = DateTime.UtcNow;
                }
            }

            await _context.SaveChangesAsync();
        }

        public async Task<IEnumerable<string>> GetCategoriesAsync(string? appCode = null)
        {
            var query = _dbSet.Where(s => !string.IsNullOrEmpty(s.Category));

            if (!string.IsNullOrEmpty(appCode))
            {
                query = query.Where(s => s.AppCode == appCode);
            }

            return await query
                .Select(s => s.Category!)
                .Distinct()
                .OrderBy(c => c)
                .ToListAsync();
        }
    }
}