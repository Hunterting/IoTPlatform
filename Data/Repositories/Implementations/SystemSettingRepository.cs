using IoTPlatform.Data;
using IoTPlatform.Data.Repositories.Interfaces;
using IoTPlatform.Models;
using Microsoft.EntityFrameworkCore;

namespace IoTPlatform.Data.Repositories.Implementations
{
    public class SystemSettingRepository : Repository<SystemSetting>, ISystemSettingRepository
    {
        public SystemSettingRepository(AppDbContext context) : base(context)
        {
        }

        public async Task<SystemSetting?> GetByKeyAsync(string settingKey)
        {
            return await _dbSet.FirstOrDefaultAsync(s => s.SettingKey == settingKey);
        }

        public async Task<SystemSetting?> GetByKeyAsync(string settingKey, string appCode)
        {
            return await _dbSet.FirstOrDefaultAsync(s => s.SettingKey == settingKey && s.AppCode == appCode);
        }

        public async Task<Dictionary<string, string>> GetAllSettingsAsync(string appCode)
        {
            var settings = await _dbSet
                .Where(s => s.AppCode == appCode)
                .Select(s => new { s.SettingKey, s.SettingValue })
                .ToListAsync();

            return settings.ToDictionary(s => s.SettingKey, s => s.SettingValue);
        }

        public async Task<bool> UpdateSettingAsync(string settingKey, string settingValue, string appCode)
        {
            var setting = await GetByKeyAsync(settingKey, appCode);
            if (setting == null)
            {
                return false;
            }

            setting.SettingValue = settingValue;
            setting.UpdatedAt = DateTime.UtcNow;
            
            return true;
        }
    }
}