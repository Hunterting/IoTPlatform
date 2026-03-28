using IoTPlatform.Models;

namespace IoTPlatform.Data.Repositories.Interfaces;

/// <summary>
/// 用户仓储接口
/// </summary>
public interface IUserRepository : IRepository<User>
{
    /// <summary>
    /// 根据邮箱获取用户
    /// </summary>
    /// <param name="email">邮箱地址</param>
    /// <returns>用户对象或null</returns>
    Task<User?> GetByEmailAsync(string email);
    
    /// <summary>
    /// 验证用户密码
    /// </summary>
    /// <param name="email">邮箱地址</param>
    /// <param name="passwordHash">密码哈希</param>
    /// <returns>是否验证成功</returns>
    Task<bool> ValidatePasswordAsync(string email, string passwordHash);
    
    /// <summary>
    /// 根据角色获取用户列表
    /// </summary>
    /// <param name="role">角色代码</param>
    /// <param name="appCode">应用代码</param>
    /// <returns>用户列表</returns>
    Task<IEnumerable<User>> GetByRoleAsync(string role, string? appCode = null);
    
    /// <summary>
    /// 根据客户ID获取用户列表
    /// </summary>
    /// <param name="customerId">客户ID</param>
    /// <param name="appCode">应用代码</param>
    /// <returns>用户列表</returns>
    Task<IEnumerable<User>> GetByCustomerIdAsync(long customerId, string? appCode = null);
    
    /// <summary>
    /// 检查邮箱是否已存在
    /// </summary>
    /// <param name="email">邮箱地址</param>
    /// <param name="excludeUserId">排除的用户ID（用于更新操作）</param>
    /// <returns>是否已存在</returns>
    Task<bool> EmailExistsAsync(string email, long? excludeUserId = null);
    
    /// <summary>
    /// 更新用户密码
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="passwordHash">新密码哈希</param>
    Task UpdatePasswordAsync(long userId, string passwordHash);
    
    /// <summary>
    /// 更新用户最后登录时间
    /// </summary>
    /// <param name="userId">用户ID</param>
    Task UpdateLastLoginAsync(long userId);
    
    /// <summary>
    /// 获取用户详情（包含关联的客户信息）
    /// </summary>
    /// <param name="id">用户ID</param>
    /// <param name="appCode">应用代码</param>
    /// <returns>用户详情或null</returns>
    Task<User?> GetUserDetailsAsync(long id, string? appCode = null);
}
