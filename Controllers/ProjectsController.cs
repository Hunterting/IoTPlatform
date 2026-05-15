using IoTPlatform.Configuration;
using IoTPlatform.DTOs.Requests;
using IoTPlatform.DTOs.Responses;
using IoTPlatform.Filters;
using IoTPlatform.Helpers;
using IoTPlatform.Models;
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

    public ProjectsController(Data.AppDbContext context, AutoMapper.IMapper mapper)
    {
        _context = context;
        _mapper = mapper;
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

            var query = _context.Projects
                .Include(p => p.WorkSummaries)
                .AsQueryable();

            // 根据角色过滤
            if (role != Roles.ADMIN && !string.IsNullOrEmpty(appCode))
            {
                query = query.Where(p => p.AppCode == appCode);
            }

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

            // 权限检查
            if (role != Roles.ADMIN && !string.IsNullOrEmpty(appCode) && project.AppCode != appCode)
            {
                return Ok(ApiResponse.Forbidden("无权访问此项目"));
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
                .Include(p => p.WorkSummaries)
                .FirstOrDefaultAsync(p => p.Id == id);

            if (project == null)
            {
                return Ok(ApiResponse.NotFound("项目不存在"));
            }

            // 权限检查
            if (role != Roles.ADMIN && !string.IsNullOrEmpty(appCode) && project.AppCode != appCode)
            {
                return Ok(ApiResponse.Forbidden("无权修改此项目"));
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
            var appCode = User.FindFirst("AppCode")?.Value;
            var role = User.FindFirst(ClaimTypes.Role)?.Value;

            var project = await _context.Projects.FindAsync(id);
            if (project == null)
            {
                return Ok(ApiResponse.NotFound("项目不存在"));
            }

            // 权限检查
            if (role != Roles.ADMIN && !string.IsNullOrEmpty(appCode) && project.AppCode != appCode)
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

            // 权限检查
            if (role != Roles.ADMIN && !string.IsNullOrEmpty(appCode) && project.AppCode != appCode)
            {
                return Ok(ApiResponse.Forbidden("无权访问此项目"));
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
            var appCode = User.FindFirst("AppCode")?.Value;
            var role = User.FindFirst(ClaimTypes.Role)?.Value;

            // 检查项目是否存在
            var project = await _context.Projects.FindAsync(projectId);
            if (project == null)
            {
                return Ok(ApiResponse.NotFound("项目不存在"));
            }

            // 权限检查
            if (role != Roles.ADMIN && !string.IsNullOrEmpty(appCode) && project.AppCode != appCode)
            {
                return Ok(ApiResponse.Forbidden("无权在此项目下创建工作纪要"));
            }

            // 如果没有提供appCode，使用项目的appCode
            if (string.IsNullOrEmpty(request.AppCode))
            {
                request.AppCode = project.AppCode;
            }

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
            var appCode = User.FindFirst("AppCode")?.Value;
            var role = User.FindFirst(ClaimTypes.Role)?.Value;

            var workSummary = await _context.WorkSummaries
                .Include(w => w.Project)
                .FirstOrDefaultAsync(w => w.Id == id);

            if (workSummary == null)
            {
                return Ok(ApiResponse.NotFound("工作纪要不存在"));
            }

            // 权限检查
            if (role != Roles.ADMIN && !string.IsNullOrEmpty(appCode) && workSummary.AppCode != appCode)
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
