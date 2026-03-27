using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace IoTPlatform.Infrastructure.JWT;

/// <summary>
/// JWT工具类
/// </summary>
public class JwtHelper
{
    private readonly IConfiguration _configuration;

    public JwtHelper(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    /// <summary>
    /// 生成JWT令牌
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="email">用户邮箱</param>
    /// <param name="role">用户角色</param>
    /// <param name="customerId">客户ID（可选）</param>
    /// <param name="appCode">应用代码（可选）</param>
    /// <param name="allowedAreaIds">允许访问的区域ID（可选）</param>
    /// <returns>JWT令牌</returns>
    public string GenerateToken(
        long userId,
        string email,
        string role,
        long? customerId = null,
        string? appCode = null,
        string? allowedAreaIds = null)
    {
        var jwtSettings = _configuration.GetSection("Jwt");
        var secretKey = jwtSettings["SecretKey"];
        var issuer = jwtSettings["Issuer"];
        var audience = jwtSettings["Audience"];
        var expirationMinutes = int.Parse(jwtSettings["ExpirationMinutes"] ?? "1440");

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey!));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId.ToString()),
            new(ClaimTypes.Email, email),
            new(ClaimTypes.Role, role),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(JwtRegisteredClaimNames.Iat, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64)
        };

        if (customerId.HasValue)
        {
            claims.Add(new Claim("CustomerId", customerId.ToString()));
        }

        if (!string.IsNullOrEmpty(appCode))
        {
            claims.Add(new Claim("AppCode", appCode));
        }

        if (!string.IsNullOrEmpty(allowedAreaIds))
        {
            claims.Add(new Claim("AllowedAreaIds", allowedAreaIds));
        }

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(expirationMinutes),
            signingCredentials: credentials
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <summary>
    /// 验证JWT令牌
    /// </summary>
    /// <param name="token">JWT令牌</param>
    /// <returns>ClaimsPrincipal</returns>
    public ClaimsPrincipal? ValidateToken(string token)
    {
        var jwtSettings = _configuration.GetSection("Jwt");
        var secretKey = jwtSettings["SecretKey"];

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey!));

        var tokenHandler = new JwtSecurityTokenHandler();
        try
        {
            var principal = tokenHandler.ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = key,
                ValidateIssuer = true,
                ValidIssuer = jwtSettings["Issuer"],
                ValidateAudience = true,
                ValidAudience = jwtSettings["Audience"],
                ValidateLifetime = true,
                ClockSkew = TimeSpan.Zero
            }, out _);

            return principal;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 从JWT令牌中提取用户ID
    /// </summary>
    /// <param name="token">JWT令牌</param>
    /// <returns>用户ID</returns>
    public long? GetUserIdFromToken(string token)
    {
        var principal = ValidateToken(token);
        if (principal == null) return null;

        var userIdClaim = principal.FindFirst(ClaimTypes.NameIdentifier);
        return userIdClaim != null && long.TryParse(userIdClaim.Value, out var userId) ? userId : null;
    }

    /// <summary>
    /// 从JWT令牌中提取角色
    /// </summary>
    /// <param name="token">JWT令牌</param>
    /// <returns>用户角色</returns>
    public string? GetRoleFromToken(string token)
    {
        var principal = ValidateToken(token);
        return principal?.FindFirst(ClaimTypes.Role)?.Value;
    }

    /// <summary>
    /// 从JWT令牌中提取客户ID
    /// </summary>
    /// <param name="token">JWT令牌</param>
    /// <returns>客户ID</returns>
    public long? GetCustomerIdFromToken(string token)
    {
        var principal = ValidateToken(token);
        if (principal == null) return null;

        var customerIdClaim = principal.FindFirst("CustomerId");
        return customerIdClaim != null && long.TryParse(customerIdClaim.Value, out var customerId) ? customerId : null;
    }

    /// <summary>
    /// 从JWT令牌中提取应用代码
    /// </summary>
    /// <param name="token">JWT令牌</param>
    /// <returns>应用代码</returns>
    public string? GetAppCodeFromToken(string token)
    {
        var principal = ValidateToken(token);
        return principal?.FindFirst("AppCode")?.Value;
    }

    /// <summary>
    /// 从JWT令牌中提取允许访问的区域ID
    /// </summary>
    /// <param name="token">JWT令牌</param>
    /// <returns>允许访问的区域ID列表</returns>
    public List<long>? GetAllowedAreaIdsFromToken(string token)
    {
        var principal = ValidateToken(token);
        if (principal == null) return null;

        var allowedAreaIdsClaim = principal.FindFirst("AllowedAreaIds");
        if (string.IsNullOrEmpty(allowedAreaIdsClaim?.Value)) return null;

        try
        {
            return allowedAreaIdsClaim.Value.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(id => long.Parse(id.Trim()))
                .ToList();
        }
        catch
        {
            return null;
        }
    }
}
