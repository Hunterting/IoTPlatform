using IoTPlatform.Configuration;
using IoTPlatform.Data;
using IoTPlatform.Filters;
using IoTPlatform.Helpers;
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

    public CustomersController(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>
    /// 判断当前用户是否为「等同超管」角色（super_admin / admin）。
    /// 在客户管理域，租户 admin 与超管一致：可见全部客户、可对任意客户增删改。
    /// </summary>
    private bool IsAdminLike()
    {
        var role = User.FindFirst(ClaimTypes.Role)?.Value;
        return role == Roles.SUPER_ADMIN || role == Roles.ADMIN;
    }

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

            // 超级管理员与租户 admin 可以查看所有客户，普通角色只能查看所属客户
            if (!IsAdminLike())
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
                    ProjectCount = _dbContext.Projects.Count(p => p.CustomerId == c.Id)
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

            // 超级管理员与租户 admin 可以访问所有客户，普通角色只能访问自己的客户
            if (!IsAdminLike())
            {
                var appCode = TenantAppCode();
                if (!string.IsNullOrEmpty(appCode))
                {
                    query = query.Where(c => c.AppCode == appCode);
                }
            }

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
                    ProjectCount = _dbContext.Projects.Count(p => p.CustomerId == c.Id)
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

            // 权限检查：admin 等同超管，普通角色严格按自身 AppCode 隔离
            if (!IsAdminLike() && customer.AppCode != TenantAppCode())
            {
                var response = ApiResponse.Forbidden("无权访问该客户");
            return Ok(response);
            }

            var deviceCount = await _dbContext.Devices.CountAsync(d => d.AppCode == customer.AppCode);
            var projectCount = customer.Projects?.Count ?? 0;

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
                ProjectCount = projectCount
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
        try
        {
            // 归属解析：admin/超管可指定任意新 AppCode（每个客户独立 AppCode）；
            // 普通角色（纵深防御，类级 PermissionAuthorize 已挡住无 CREATE_CUSTOMERS 权限者）
            // 不采信前端传入的 AppCode，强制使用自身 claim，防止伪造跨租户归属。
            string? effectiveAppCode;
            if (IsAdminLike())
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
                Status = "active"
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
                ProjectCount = 0
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
        try
        {
            var customer = await _dbContext.Customers.FindAsync(id);
            if (customer == null)
            {
                var response = ApiResponse.NotFound("客户不存在");
            return Ok(response);
            }

            // 权限检查：admin 等同超管，普通角色严格按自身 AppCode 隔离
            if (!IsAdminLike() && customer.AppCode != TenantAppCode())
            {
                var response = ApiResponse.Forbidden("无权修改该客户");
            return Ok(response);
            }

            customer.Name = request.Name;
            customer.ContactPerson = request.Contact;
            customer.Phone = request.Phone;
            customer.Address = request.Address;
            customer.Status = request.Status;
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
                ProjectCount = projectCount
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

            // 权限检查：admin 等同超管，普通角色严格按自身 AppCode 隔离
            if (!IsAdminLike() && customer.AppCode != TenantAppCode())
            {
                var forbiddenResponse = ApiResponse.Forbidden("无权删除该客户");
                return Ok(forbiddenResponse);
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
}

public class UpdateCustomerRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Contact { get; set; }
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public string Status { get; set; } = string.Empty;
}
