using IoTPlatform.Configuration;
using IoTPlatform.Data;
using IoTPlatform.Filters;
using IoTPlatform.Helpers;
using IoTPlatform.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace IoTPlatform.Controllers;

/// <summary>
/// 客户管理控制器
/// </summary>
[ApiController]
[Route("api/v1/customers")]
[PermissionAuthorize(Permissions.VIEW_CUSTOMERS)]
public class CustomersController : ControllerBase
{
    private readonly AppDbContext _dbContext;
    private readonly ICustomerScopeService _customerScope;

    public CustomersController(AppDbContext dbContext, ICustomerScopeService customerScope)
    {
        _dbContext = dbContext;
        _customerScope = customerScope;
    }

    /// <summary>统一超管判据（设计 §7.2-2）。</summary>
    private bool IsSuperAdmin() =>
        Roles.IsSuperAdmin(User.FindFirst(ClaimTypes.Role)?.Value);

    /// <summary>租户管理员判据；customer_admin 不纳入集合分支。</summary>
    private bool IsTenantAdmin() =>
        User.FindFirst(ClaimTypes.Role)?.Value == Roles.ADMIN;

    /// <summary>
    /// 读取当前用户的租户 AppCode claim（可能为空）。
    /// </summary>
    private string? TenantAppCode() => User.FindFirst("AppCode")?.Value;

    /// <summary>
    /// 获取客户列表
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResponse<CustomerDto>>>> GetCustomers(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? keyword = null)
    {
        try
        {
            var query = _dbContext.Customers.AsQueryable();

            // 【多客户授权 T04 / §5.4】按 ScopeMode 三分支，绝不按集合空否分支：
            // All 的空集合 = 不过滤（超管全量）；Multi 的空集合 = 授权为空（空列表）。
            var scope = await _customerScope.ResolveAsync(User);
            if (scope.Mode == CustomerScopeMode.All)
            {
                // 超管：不加 Where，全量不变。
            }
            else if (scope.Mode == CustomerScopeMode.Multi)
            {
                var managedIds = scope.CustomerIds.Distinct().ToList();
                query = query.Where(c => managedIds.Contains(c.Id));
            }
            else
            {
                var appCode = TenantAppCode();
                if (!string.IsNullOrEmpty(appCode))
                {
                    query = query.Where(c => c.AppCode == appCode);
                }
            }

            // 关键词搜索
            if (!string.IsNullOrEmpty(keyword))
            {
                query = query.Where(c =>
                    c.Name.Contains(keyword) ||
                    c.Code.Contains(keyword) ||
                    c.ContactPerson != null && c.ContactPerson.Contains(keyword));
            }

            // 配额需求 R07：MaxDeviceCount 仅超管下发。投影在 IQueryable 上执行，
            // 超管判据必须先算成捕获的本地 bool（EF Core 可参数化），不可在 Select 内调用自定义方法。
            var quotaVisible = Roles.IsSuperAdmin(User.FindFirst(ClaimTypes.Role)?.Value);
            var totalCount = await query.CountAsync();
            var customers = await query
                .OrderByDescending(c => c.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(c => new CustomerDto
                {
                    Id = c.Id,
                    Name = c.Name,
                    Code = c.Code,
                    AppCode = c.AppCode,
                    Contact = c.ContactPerson,
                    Phone = c.Phone,
                    Address = c.Address,
                    Status = c.Status,
                    CreatedAt = c.CreatedAt,
                    DeviceCount = _dbContext.Devices.Count(d => d.AppCode == c.AppCode),
                    ProjectCount = _dbContext.Projects.Count(p => p.CustomerId == c.Id),
                    MaxDeviceCount = quotaVisible ? (int?)c.MaxDeviceCount : null
                })
                .ToListAsync();

            var pagedResponse = PagedResponse<CustomerDto>.Create(customers, totalCount, page, pageSize);
            return ApiResponse<PagedResponse<CustomerDto>>.Success(pagedResponse);
        }
        catch (Exception ex)
        {
            return ApiResponse<PagedResponse<CustomerDto>>.Error(ex.Message);
        }
    }

    /// <summary>
    /// 获取可访问的客户列表（用于超级管理员切换租户）
    /// </summary>
    [HttpGet("accessible")]
    public async Task<ActionResult<ApiResponse<List<CustomerDto>>>> GetAccessibleCustomers()
    {
        try
        {
            var query = _dbContext.Customers.AsQueryable();

            // 【多客户授权 T04 / §5.4】与 GetCustomers 同语义：admin 返回授权客户集合。
            var scope = await _customerScope.ResolveAsync(User);
            if (scope.Mode == CustomerScopeMode.All)
            {
                // 超管：全量，不变。
            }
            else if (scope.Mode == CustomerScopeMode.Multi)
            {
                var managedIds = scope.CustomerIds.Distinct().ToList();
                query = query.Where(c => managedIds.Contains(c.Id));
            }
            else
            {
                var appCode = TenantAppCode();
                if (!string.IsNullOrEmpty(appCode))
                {
                    query = query.Where(c => c.AppCode == appCode);
                }
            }

            // 配额需求 R07：仅超管下发 MaxDeviceCount（捕获本地 bool 供 EF 参数化）。
            var quotaVisible = Roles.IsSuperAdmin(User.FindFirst(ClaimTypes.Role)?.Value);
            var customers = await query
                .OrderBy(c => c.Name)
                .Select(c => new CustomerDto
                {
                    Id = c.Id,
                    Name = c.Name,
                    Code = c.Code,
                    AppCode = c.AppCode,
                    Contact = c.ContactPerson,
                    Phone = c.Phone,
                    Address = c.Address,
                    Status = c.Status,
                    CreatedAt = c.CreatedAt,
                    DeviceCount = _dbContext.Devices.Count(d => d.AppCode == c.AppCode),
                    ProjectCount = _dbContext.Projects.Count(p => p.CustomerId == c.Id),
                    MaxDeviceCount = quotaVisible ? (int?)c.MaxDeviceCount : null
                })
                .ToListAsync();

            return ApiResponse<List<CustomerDto>>.Success(customers);
        }
        catch (Exception ex)
        {
            return ApiResponse<List<CustomerDto>>.Error(ex.Message);
        }
    }

    /// <summary>
    /// 获取客户详情
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<ApiResponse<CustomerDetailDto>>> GetCustomer(long id)
    {
        try
        {
            var customer = await _dbContext.Customers
                .Include(c => c.Projects)
                .FirstOrDefaultAsync(c => c.Id == id);

            if (customer == null)
            {
                var response = ApiResponse.NotFound("客户不存在");
            return Ok(response);
            }

            // 【多客户授权 T04 / §5.4】三分支详情校验。
            var scope = await _customerScope.ResolveAsync(User);
            if (scope.Mode == CustomerScopeMode.Multi && !scope.CustomerIds.Contains(id))
            {
                var response = ApiResponse.Forbidden("无权访问该客户");
                return Ok(response);
            }

            if (scope.Mode == CustomerScopeMode.Single && customer.AppCode != TenantAppCode())
            {
                var response = ApiResponse.Forbidden("无权访问该客户");
                return Ok(response);
            }

            var deviceCount = await _dbContext.Devices.CountAsync(d => d.AppCode == customer.AppCode);
            var projectCount = customer.Projects?.Count ?? 0;

            // 配额需求 R07：详情投影仅超管赋真实值，非超管保持 int? 默认 null（防抓包）。
            var quotaVisible = Roles.IsSuperAdmin(User.FindFirst(ClaimTypes.Role)?.Value);
            var dto = new CustomerDetailDto
            {
                Id = customer.Id,
                Name = customer.Name,
                Code = customer.Code,
                AppCode = customer.AppCode,
                Contact = customer.ContactPerson,
                Phone = customer.Phone,
                Address = customer.Address,
                Status = customer.Status,
                CreatedAt = customer.CreatedAt,
                DeviceCount = deviceCount,
                ProjectCount = projectCount,
                MaxDeviceCount = quotaVisible ? (int?)customer.MaxDeviceCount : null
            };

            return ApiResponse<CustomerDetailDto>.Success(dto);
        }
        catch (Exception ex)
        {
            var response = ApiResponse.Error(ex.Message);
            return Ok(response);
        }
    }

    /// <summary>
    /// 创建客户
    /// </summary>
    [HttpPost]
    [PermissionAuthorize(Permissions.CREATE_CUSTOMERS)]
    public async Task<ActionResult<ApiResponse<CustomerDto>>> CreateCustomer([FromBody] CreateCustomerRequest request)
    {
        // ── 配额需求 R01：仅超级管理员可创建客户/租户 ──
        // 服务端硬校验，防止绕过前端权限表直调 API；写法仿 DevicesController.CreateDevice:132-143。
        // 全站 HTTP 200 约定：业务失败仍返回 Ok(ApiResponse)，只是业务码为 403。
        var role = User.FindFirst(ClaimTypes.Role)?.Value;
        if (!Roles.IsSuperAdmin(role))
        {
            var forbidden = ApiResponse<CustomerDto>.Fail(403, "仅超级管理员可创建客户/租户。");
            return Ok(forbidden);
        }

        try
        {
            // 归属解析：admin/超管可指定任意新 AppCode（每个客户独立 AppCode）；
            // 普通角色（纵深防御，类级 PermissionAuthorize 已挡住无 CREATE_CUSTOMERS 权限者）
            // 不采信前端传入的 AppCode，强制使用自身 claim，防止伪造跨租户归属。
            string? effectiveAppCode;
            if (IsSuperAdmin())
            {
                effectiveAppCode = request.AppCode;
            }
            else
            {
                var tenantAppCode = TenantAppCode();
                if (!string.IsNullOrEmpty(request.AppCode) && request.AppCode != tenantAppCode)
                {
                    var forbiddenResponse = ApiResponse.Forbidden("无权为该租户创建客户");
                    return Ok(forbiddenResponse);
                }
                effectiveAppCode = tenantAppCode;
            }

            if (string.IsNullOrEmpty(effectiveAppCode))
            {
                var badRequestResponse = ApiResponse.BadRequest("应用代码不能为空");
                return Ok(badRequestResponse);
            }

            // 检查代码是否已存在
            var exists = await _dbContext.Customers
                .AnyAsync(c => c.Code == request.Code || c.AppCode == effectiveAppCode);

            if (exists)
            {
                var response = ApiResponse.BadRequest("客户代码或应用代码已存在");
            return Ok(response);
            }

            var customer = new Models.Customer
            {
                Name = request.Name,
                Code = request.Code,
                AppCode = effectiveAppCode,
                ContactPerson = request.Contact,
                Phone = request.Phone,
                Address = request.Address,
                Status = "active",
                // 决策 5：新建默认 0（不限）；不改实体初始化器，显式赋值精确控制本入口。
                MaxDeviceCount = request.MaxDeviceCount ?? 0
            };

            _dbContext.Customers.Add(customer);
            await _dbContext.SaveChangesAsync();

            var dto = new CustomerDto
            {
                Id = customer.Id,
                Name = customer.Name,
                Code = customer.Code,
                AppCode = effectiveAppCode,
                Contact = customer.ContactPerson,
                Phone = customer.Phone,
                Address = customer.Address,
                Status = customer.Status,
                CreatedAt = customer.CreatedAt,
                DeviceCount = 0,
                ProjectCount = 0,
                // 本端点顶部已做超管硬校验，创建响应始终可回显真实数值。
                MaxDeviceCount = customer.MaxDeviceCount
            };

            return ApiResponse<CustomerDto>.Success(dto, "客户创建成功");
        }
        catch (Exception ex)
        {
            var response = ApiResponse.Error(ex.Message);
            return Ok(response);
        }
    }

    /// <summary>
    /// 更新客户
    /// </summary>
    [HttpPut("{id}")]
    [PermissionAuthorize(Permissions.UPDATE_CUSTOMERS)]
    public async Task<ActionResult<ApiResponse<CustomerDto>>> UpdateCustomer(long id, [FromBody] UpdateCustomerRequest request)
    {
        // 配额需求 R07 / 裁决 B：MaxDeviceCount 仅超管可写；非超管即使伪造该字段也静默忽略，
        // 其它字段（名称/联系人/电话/地址/状态）照常保存，不返回 403。
        var quotaEditable = Roles.IsSuperAdmin(User.FindFirst(ClaimTypes.Role)?.Value);
        try
        {
            var customer = await _dbContext.Customers.FindAsync(id);
            if (customer == null)
            {
                var response = ApiResponse.NotFound("客户不存在");
            return Ok(response);
            }

            // 【多客户授权 T04 / §5.4】超管放行；admin 仅授权客户可改；下级原 AppCode 校验。
            var scope = await _customerScope.ResolveAsync(User);
            if (scope.Mode == CustomerScopeMode.Multi && !scope.CustomerIds.Contains(id))
            {
                var response = ApiResponse.Forbidden("无权访问该客户");
                return Ok(response);
            }

            if (scope.Mode == CustomerScopeMode.Single && customer.AppCode != TenantAppCode())
            {
                var response = ApiResponse.Forbidden("无权修改该客户");
                return Ok(response);
            }

            customer.Name = request.Name;
            customer.ContactPerson = request.Contact;
            customer.Phone = request.Phone;
            customer.Address = request.Address;
            customer.Status = request.Status;
            if (quotaEditable && request.MaxDeviceCount.HasValue)
            {
                customer.MaxDeviceCount = request.MaxDeviceCount.Value;
            }
            customer.UpdatedAt = DateTime.UtcNow;

            await _dbContext.SaveChangesAsync();

            var deviceCount = await _dbContext.Devices.CountAsync(d => d.AppCode == customer.AppCode);
            var projectCount = await _dbContext.Projects.CountAsync(p => p.CustomerId == customer.Id);

            var dto = new CustomerDto
            {
                Id = customer.Id,
                Name = customer.Name,
                Code = customer.Code,
                AppCode = customer.AppCode,
                Contact = customer.ContactPerson,
                Phone = customer.Phone,
                Address = customer.Address,
                Status = customer.Status,
                CreatedAt = customer.CreatedAt,
                DeviceCount = deviceCount,
                ProjectCount = projectCount,
                MaxDeviceCount = quotaEditable ? (int?)customer.MaxDeviceCount : null
            };

            return ApiResponse<CustomerDto>.Success(dto, "客户更新成功");
        }
        catch (Exception ex)
        {
            var response = ApiResponse.Error(ex.Message);
            return Ok(response);
        }
    }

    /// <summary>
    /// 删除客户
    /// </summary>
    [HttpDelete("{id}")]
    [PermissionAuthorize(Permissions.DELETE_CUSTOMERS)]
    public async Task<ActionResult<ApiResponse>> DeleteCustomer(long id)
    {
        try
        {
            var customer = await _dbContext.Customers.FindAsync(id);
            if (customer == null)
            {
                var response = ApiResponse.NotFound("客户不存在");
            return Ok(response);
            }

            // 【多客户授权 T04 / §5.4】超管放行；admin 仅授权客户可删；下级原 AppCode 校验。
            var scope = await _customerScope.ResolveAsync(User);
            if (scope.Mode == CustomerScopeMode.Multi && !scope.CustomerIds.Contains(id))
            {
                var forbiddenResponse = ApiResponse.Forbidden("无权访问该客户");
                return Ok(forbiddenResponse);
            }

            if (scope.Mode == CustomerScopeMode.Single && customer.AppCode != TenantAppCode())
            {
                var forbiddenResponse = ApiResponse.Forbidden("无权删除该客户");
                return Ok(forbiddenResponse);
            }

            // 【T01 衔接】user_customers.CustomerId 的 FK 为 ON DELETE RESTRICT；
            // 必须先拒绝带授权引用的删除，否则会撞外键 500，且避免留下悬空授权的语义漏洞。
            var hasGrants = await _dbContext.UserCustomers
                .IgnoreQueryFilters()
                .AnyAsync(uc => uc.CustomerId == id);
            if (hasGrants)
            {
                var response = ApiResponse.BadRequest("该客户已授权给租户管理员，请先撤销授权后再删除。");
                return Ok(response);
            }

            // 检查是否有关联数据
            var hasDevices = await _dbContext.Devices.AnyAsync(d => d.AppCode == customer.AppCode);
            var hasUsers = await _dbContext.Users.AnyAsync(u => u.CustomerId == customer.Id);
            var hasProjects = await _dbContext.Projects.AnyAsync(p => p.CustomerId == customer.Id);

            if (hasDevices || hasUsers || hasProjects)
            {
                var response = ApiResponse.BadRequest("客户有关联数据，无法删除");
            return Ok(response);
            }

            _dbContext.Customers.Remove(customer);
            await _dbContext.SaveChangesAsync();

            return ApiResponse.Success("客户删除成功");
        }
        catch (Exception ex)
        {
            var response = ApiResponse.Error(ex.Message);
            return Ok(response);
        }
    }
}

// DTOs
public class CustomerDto
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string AppCode { get; set; } = string.Empty;
    public string? Contact { get; set; }
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public int DeviceCount { get; set; }
    public int ProjectCount { get; set; }
    // 配额需求 R07：nullable 是裁剪关键——非超管投影不赋值即为 null，不下发真实数值。
    public int? MaxDeviceCount { get; set; }
}

public class CustomerDetailDto : CustomerDto
{
    public new int ProjectCount { get; set; }
}

public class CreateCustomerRequest
{
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string AppCode { get; set; } = string.Empty;
    public string? Contact { get; set; }
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public int? MaxDeviceCount { get; set; }
}

public class UpdateCustomerRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Contact { get; set; }
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public string Status { get; set; } = string.Empty;
    public int? MaxDeviceCount { get; set; }
}
