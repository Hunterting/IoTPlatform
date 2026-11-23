using IoTPlatform.Configuration;
using IoTPlatform.DTOs.Requests;
using IoTPlatform.DTOs.Responses;
using IoTPlatform.Filters;
using IoTPlatform.Helpers;
using IoTPlatform.Models;
using IoTPlatform.Services;
using IoTPlatform.Services.Guards;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace IoTPlatform.Controllers;

/// <summary>
/// 项目管理控制器
/// </summary>
[ApiController]
[Route("api/v1/projects")]
[PermissionAuthorize(Permissions.VIEW_PROJECTS)]
public class ProjectsController : ControllerBase
{
    private readonly Data.AppDbContext _context;
    private readonly AutoMapper.IMapper _mapper;
    private readonly ICustomerScopeService _customerScope;
    private readonly ICustomerScopeGuard _scopeGuard;

    public ProjectsController(
        Data.AppDbContext context,
        AutoMapper.IMapper mapper,
        ICustomerScopeService customerScope,
        ICustomerScopeGuard scopeGuard)
    {
        _context = context;
        _mapper = mapper;
        _customerScope = customerScope ?? throw new ArgumentNullException(nameof(customerScope));
        _scopeGuard = scopeGuard ?? throw new ArgumentNullException(nameof(scopeGuard));
    }

    /// <summary>
    /// 获取项目列表
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResponse<ProjectResponse>>>> GetProjects(
        [FromQuery] long? customerId = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        try
        {
            var appCode = User.FindFirst("AppCode")?.Value;
            var role = User.FindFirst(ClaimTypes.Role)?.Value;
            var listScope = await this.ResolveListScopeAsync(_customerScope);

            var query = _context.Projects
                .Include(p => p.WorkSummaries)
                .AsQueryable();

            // 根据角色过滤
            if (role != Roles.ADMIN && !string.IsNullOrEmpty(appCode))
            {
                query = query.Where(p => p.AppCode == appCode);
            }

            // B4/M5：admin 授权客户集合（DB 权威）真正下推，替代原来 role==ADMIN 的全量放行。
            query = query.ApplyQueryScope(appCode, listScope.CustomerIds, listScope.AppCodes);

            // 根据客户ID过滤
            if (customerId.HasValue)
            {
                query = query.Where(p => p.CustomerId == customerId.Value);
            }

            var total = await query.CountAsync();
            var items = await query
                .OrderByDescending(p => p.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var response = new PagedResponse<ProjectResponse>
            {
                Items = _mapper.Map<List<ProjectResponse>>(items),
                TotalCount = total,
                Page = page,
                PageSize = pageSize
            };

            return ApiResponse<PagedResponse<ProjectResponse>>.Success(response);
        }
        catch (Exception ex)
        {
            return ApiResponse<PagedResponse<ProjectResponse>>.Error(ex.Message);
        }
    }

    /// <summary>
    /// 获取项目详情
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<ApiResponse<ProjectResponse>>> GetProject(long id)
    {
        try
        {
            var appCode = User.FindFirst("AppCode")?.Value;
            var role = User.FindFirst(ClaimTypes.Role)?.Value;

            var project = await _context.Projects
                .Include(p => p.WorkSummaries)
                .FirstOrDefaultAsync(p => p.Id == id);

            if (project == null)
            {
                return Ok(ApiResponse.NotFound("项目不存在"));
            }

            // B4/M6：原 role==ADMIN 全量放行已移除，改走 DB 权威的 scope 判据。
            if (!await _scopeGuard.CanAccessOwnedEntityAsync(User, project.CustomerId, project.AppCode))
            {
                return Ok(ApiResponse<ProjectResponse>.Fail(403, "无权访问此项目"));
            }

            return ApiResponse<ProjectResponse>.Success(_mapper.Map<ProjectResponse>(project));
        }
        catch (Exception ex)
        {
            return ApiResponse<ProjectResponse>.Error(ex.Message);
        }
    }

    /// <summary>
    /// 创建项目
    /// </summary>
    [HttpPost]
    [PermissionAuthorize(Permissions.MANAGE_PROJECTS)]
    public async Task<ActionResult<ApiResponse<ProjectResponse>>> CreateProject([FromBody] CreateProjectRequest request)
    {
        try
        {
            // 【M1】admin 自身无 AppCode claim：先以 DB 授权集合判定目标客户，再做任何写库。
            if (Roles.ADMIN == User.FindFirst(ClaimTypes.Role)?.Value)
            {
                if (request.CustomerId <= 0)
                {
                    return Ok(ApiResponse.BadRequest("租户管理员必须在授权客户下创建项目。"));
                }
                if (!await _customerScope.CanAccessCustomerAsync(User, request.CustomerId))
                {
                    return Ok(ApiResponse.Forbidden("无权在该客户下创建项目"));
                }

                var resolvedAppCode = await _customerScope.ResolveAppCodeByCustomerIdAsync(request.CustomerId);
                if (string.IsNullOrWhiteSpace(resolvedAppCode))
                {
                    return Ok(ApiResponse.BadRequest("目标客户缺少 AppCode"));
                }

                // 归属由服务端派生，绝不采信请求体的 AppCode。
                request.AppCode = resolvedAppCode;
            }

            var appCode = User.FindFirst("AppCode")?.Value;

            // 检查客户是否存在
            var customer = await _context.Customers.FindAsync(request.CustomerId);
            if (customer == null)
            {
                return Ok(ApiResponse.BadRequest("客户不存在"));
            }

            // 如果没有提供appCode，使用当前用户的appCode
            if (string.IsNullOrEmpty(request.AppCode))
            {
                request.AppCode = appCode;
            }

            var project = _mapper.Map<Project>(request);
            project.CreatedAt = DateTime.UtcNow;
            project.UpdatedAt = DateTime.UtcNow;

            _context.Projects.Add(project);
            await _context.SaveChangesAsync();

            // 重新加载关联数据
            await _context.Entry(project)
                .Collection(p => p.WorkSummaries!)
                .LoadAsync();

            return ApiResponse<ProjectResponse>.Success(
                _mapper.Map<ProjectResponse>(project),
                "项目创建成功");
        }
        catch (Exception ex)
        {
            return ApiResponse<ProjectResponse>.Error(ex.Message);
        }
    }

    /// <summary>
    /// 更新项目
    /// </summary>
    [HttpPut("{id}")]
    [PermissionAuthorize(Permissions.MANAGE_PROJECTS)]
    public async Task<ActionResult<ApiResponse<ProjectResponse>>> UpdateProject(
        long id,
        [FromBody] UpdateProjectRequest request)
    {
        try
        {
            var appCode = User.FindFirst("AppCode")?.Value;
            var role = User.FindFirst(ClaimTypes.Role)?.Value;

            var project = await _context.Projects
                .IgnoreQueryFilters()
                .Include(p => p.WorkSummaries)
                .FirstOrDefaultAsync(p => p.Id == id);

            if (project == null)
            {
                return Ok(ApiResponse.NotFound("项目不存在"));
            }

            // 【M3】三分支判据：admin 以 DB 授权集合为准，admin 自身 AppCode=null 不再恒真跳过。
            var scope = await _customerScope.ResolveAsync(User);
            if (!await _scopeGuard.CanAccessOwnedEntityAsync(User, project.CustomerId, project.AppCode))
            {
                return Ok(ApiResponse.Forbidden("无权修改此项目"));
            }

            // M3c：非超管冻结归属，禁止通过 body 把项目迁到其它租户。
            if (scope.Mode != CustomerScopeMode.All)
            {
                request.AppCode = project.AppCode;
            }

            _mapper.Map(request, project);
            project.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            return ApiResponse<ProjectResponse>.Success(
                _mapper.Map<ProjectResponse>(project),
                "项目更新成功");
        }
        catch (Exception ex)
        {
            return ApiResponse<ProjectResponse>.Error(ex.Message);
        }
    }

    /// <summary>
    /// 删除项目
    /// </summary>
    [HttpDelete("{id}")]
    [PermissionAuthorize(Permissions.MANAGE_PROJECTS)]
    public async Task<ActionResult<ApiResponse>> DeleteProject(long id)
    {
        try
        {
            var project = await _context.Projects
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(p => p.Id == id);
            if (project == null)
            {
                return Ok(ApiResponse.NotFound("项目不存在"));
            }

            // 【M3】删除前完成归属判定，无写库、无副作用。
            if (!await _scopeGuard.CanAccessOwnedEntityAsync(User, project.CustomerId, project.AppCode))
            {
                return Ok(ApiResponse.Forbidden("无权删除此项目"));
            }

            _context.Projects.Remove(project);
            await _context.SaveChangesAsync();

            return ApiResponse.Success("项目删除成功");
        }
        catch (Exception ex)
        {
            return ApiResponse.Error(ex.Message);
        }
    }

    #region 工作纪要管理

    /// <summary>
    /// 获取项目的工作纪要列表
    /// </summary>
    [HttpGet("{projectId}/work-summaries")]
    public async Task<ActionResult<ApiResponse<List<WorkSummaryResponse>>>> GetWorkSummaries(long projectId)
    {
        try
        {
            var appCode = User.FindFirst("AppCode")?.Value;
            var role = User.FindFirst(ClaimTypes.Role)?.Value;

            // 检查项目是否存在
            var project = await _context.Projects.FindAsync(projectId);
            if (project == null)
            {
                return Ok(ApiResponse.NotFound("项目不存在"));
            }

            // B4/M6：工作纪要跟随项目归属，admin 集合外项目 403。
            if (!await _scopeGuard.CanAccessOwnedEntityAsync(User, project.CustomerId, project.AppCode))
            {
                return Ok(ApiResponse<List<WorkSummaryResponse>>.Fail(403, "无权访问此项目"));
            }

            var workSummaries = await _context.WorkSummaries
                .Where(w => w.ProjectId == projectId)
                .OrderByDescending(w => w.Date)
                .ToListAsync();

            return ApiResponse<List<WorkSummaryResponse>>.Success(
                _mapper.Map<List<WorkSummaryResponse>>(workSummaries));
        }
        catch (Exception ex)
        {
            return ApiResponse<List<WorkSummaryResponse>>.Error(ex.Message);
        }
    }

    /// <summary>
    /// 创建工作纪要
    /// </summary>
    [HttpPost("{projectId}/work-summaries")]
    [PermissionAuthorize(Permissions.MANAGE_PROJECTS)]
    public async Task<ActionResult<ApiResponse<WorkSummaryResponse>>> CreateWorkSummary(
        long projectId,
        [FromBody] CreateWorkSummaryRequest request)
    {
        try
        {
            // 检查项目是否存在（点查不被租户全局过滤器遮挡）
            var project = await _context.Projects
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(p => p.Id == projectId);
            if (project == null)
            {
                return Ok(ApiResponse.NotFound("项目不存在"));
            }

            // 【M3】按项目归属判定，guard 在 WorkSummaries.Add 之前。
            var scope = await _customerScope.ResolveAsync(User);
            if (!await _scopeGuard.CanAccessOwnedEntityAsync(User, project.CustomerId, project.AppCode))
            {
                return Ok(ApiResponse.Forbidden("无权在此项目下创建工作纪要"));
            }

            // 归属冻结：非超管无论请求体传什么 AppCode，一律用项目权威 AppCode。
            request.AppCode = scope.Mode == CustomerScopeMode.All && !string.IsNullOrEmpty(request.AppCode)
                ? request.AppCode
                : project.AppCode;

            var workSummary = _mapper.Map<WorkSummary>(request);
            workSummary.ProjectId = projectId;

            _context.WorkSummaries.Add(workSummary);
            await _context.SaveChangesAsync();

            return ApiResponse<WorkSummaryResponse>.Success(
                _mapper.Map<WorkSummaryResponse>(workSummary),
                "工作纪要创建成功");
        }
        catch (Exception ex)
        {
            return ApiResponse<WorkSummaryResponse>.Error(ex.Message);
        }
    }

    /// <summary>
    /// 删除工作纪要
    /// </summary>
    [HttpDelete("work-summaries/{id}")]
    [PermissionAuthorize(Permissions.MANAGE_PROJECTS)]
    public async Task<ActionResult<ApiResponse>> DeleteWorkSummary(long id)
    {
        try
        {
            var workSummary = await _context.WorkSummaries
                .IgnoreQueryFilters()
                .Include(w => w.Project)
                .FirstOrDefaultAsync(w => w.Id == id);

            if (workSummary == null)
            {
                return Ok(ApiResponse.NotFound("工作纪要不存在"));
            }

            // 【M3】工作纪要无 CustomerId 列：用所属项目的 CustomerId + 自身 AppCode 判定。
            if (!await _scopeGuard.CanAccessOwnedEntityAsync(
                    User, workSummary.Project?.CustomerId, workSummary.AppCode))
            {
                return Ok(ApiResponse.Forbidden("无权删除此工作纪要"));
            }

            _context.WorkSummaries.Remove(workSummary);
            await _context.SaveChangesAsync();

            return ApiResponse.Success("工作纪要删除成功");
        }
        catch (Exception ex)
        {
            return ApiResponse.Error(ex.Message);
        }
    }

    #endregion
}
