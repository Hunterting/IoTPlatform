// B3 / M2 + M3 + M10：配置类写端点 + 提权面授权收口测试。
//
// M2：请求体 AppCode 只作待校验目标；admin 必须经 DB 授权集合校验，归属由服务端派生。
// M3：按 id 改删/启停前的归属守卫；共享字典读放行、写/删仅超管；协议配置改归属即 403。
// M10：admin 建角色必须落到授权客户且 AppCode 非 null，权限不得超出自身有效权限。

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using IoTPlatform.Configuration;
using IoTPlatform.Controllers;
using IoTPlatform.Data;
using IoTPlatform.Data.Repositories.Implementations;
using IoTPlatform.DTOs.Requests;
using IoTPlatform.DTOs.Responses;
using IoTPlatform.Helpers;
using IoTPlatform.Models;
using IoTPlatform.Services;
using IoTPlatform.Services.Guards;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace IoTPlatform.AnSheng.Tests;

/// <summary>
/// B3 批次模式测试（设计 §2.3 / §2.4 / §2.11 / §3.3 / §3.5）。
/// 使用真实 InMemory DbContext + 真实范围服务/守卫/仓储/服务/控制器，只有日志器是替身。
/// </summary>
public sealed class PatternM2_M3_M10_Tests : IDisposable
{
    private const long AdminId = 10;
    private const long CustomerOne = 1;
    private const long CustomerTwo = 2;
    private const string AppOne = "customer_001";
    private const string AppTwo = "customer_002";

    private readonly AppDbContext _db;
    private readonly CustomerScopeService _scope;
    private readonly CustomerScopeGuard _guard;
    private readonly ProtocolConfigsController _protocolConfigs;
    private readonly DictionariesController _dictionaries;
    private readonly ArchivesController _archives;
    private readonly DataRulesController _dataRules;
    private readonly ETLTasksController _etlTasks;
    private readonly SettingsController _settings;
    private readonly RolesController _roles;
    private readonly RoleService _roleService;

    private readonly long _ownedProtocolId;
    private readonly long _foreignProtocolId;
    private readonly long _sharedDictionaryTypeId;
    private readonly long _ownedDictionaryTypeId;
    private readonly long _foreignArchiveId;
    private readonly long _foreignRuleId;
    private readonly long _foreignTaskId;

    public PatternM2_M3_M10_Tests()
    {
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"b3-m2m3m10-{Guid.NewGuid():N}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

        foreach (var id in new[] { CustomerOne, CustomerTwo })
        {
            _db.Customers.Add(new Customer
            {
                Id = id,
                Name = $"客户{id}",
                Code = $"C{id:000}",
                AppCode = id == CustomerOne ? AppOne : AppTwo,
                Status = "active",
                MaxDeviceCount = 100,
                MaxUserCount = 100,
                MaxAreaCount = 100,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }

        var ownedProtocol = new ProtocolConfig { Name = "本租户协议", Type = "mqtt", Status = "inactive", AppCode = AppOne };
        var foreignProtocol = new ProtocolConfig { Name = "他租户协议", Type = "mqtt", Status = "inactive", AppCode = AppTwo };
        _db.ProtocolConfigs.AddRange(ownedProtocol, foreignProtocol);

        var sharedType = new DictionaryTypeConfig
        {
            Code = "SHARED_TYPE", Name = "共享类型", AppCode = "system", IsActive = true, SortOrder = 1,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        };
        var ownedType = new DictionaryTypeConfig
        {
            Code = "OWN_TYPE", Name = "本租户类型", AppCode = AppOne, IsActive = true, SortOrder = 2,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        };
        _db.DictionaryTypeConfigs.AddRange(sharedType, ownedType);

        var foreignArchive = new Archive { Name = "他租户档案", Type = "document", AppCode = AppTwo };
        var foreignRule = new DataRule { Name = "他租户规则", AppCode = AppTwo };
        var foreignTask = new ETLTask { Name = "他租户任务", Status = "paused", AppCode = AppTwo };
        _db.Archives.Add(foreignArchive);
        _db.DataRules.Add(foreignRule);
        _db.EtlTasks.Add(foreignTask);

        // admin 的有效权限：仅这两个；请求中若出现 DELETE_CUSTOMERS 即为越权授予。
        _db.Roles.Add(new Role
        {
            Code = Roles.ADMIN,
            Name = "租户管理员",
            Permissions = System.Text.Json.JsonSerializer.Serialize(new[]
            {
                Permissions.VIEW_ROLES, Permissions.CREATE_ROLES
            }),
            IsSystem = true,
            AppCode = null,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });

        _db.SystemSettings.AddRange(
            new SystemSetting { Key = "shared.key", Value = "v1", AppCode = null, Category = "common" },
            new SystemSetting { Key = "tenant.key", Value = "v2", AppCode = AppTwo, Category = "common" });

        _db.SaveChanges();

        _ownedProtocolId = ownedProtocol.Id;
        _foreignProtocolId = foreignProtocol.Id;
        _sharedDictionaryTypeId = sharedType.Id;
        _ownedDictionaryTypeId = ownedType.Id;
        _foreignArchiveId = foreignArchive.Id;
        _foreignRuleId = foreignRule.Id;
        _foreignTaskId = foreignTask.Id;

        var grants = new UserCustomerRepository(_db);
        grants.SyncAsync(AdminId, new[] { CustomerOne }, 1).GetAwaiter().GetResult();
        _db.SaveChanges();
        _db.ChangeTracker.Clear();

        _scope = new CustomerScopeService(
            grants, new CustomerRepository(_db), new DeviceRepository(_db), new ProjectRepository(_db));
        _guard = new CustomerScopeGuard(_scope);

        var unitOfWork = new UnitOfWork(_db);
        var devices = new DeviceRepository(_db);
        var areas = new AreaRepository(_db);

        var protocolService = new ProtocolConfigService(
            new ProtocolConfigRepository(_db), unitOfWork, null!, null!,
            null, null, null, _guard);
        _protocolConfigs = new ProtocolConfigsController(protocolService, _scope);

        _dictionaries = new DictionariesController(
            new DictionaryService(new DictionaryRepository(_db), unitOfWork, _guard), _scope);
        _archives = new ArchivesController(
            new ArchiveService(new ArchiveRepository(_db), devices, unitOfWork, _guard), _scope);
        _dataRules = new DataRulesController(
            new DataRuleService(new DataRuleRepository(_db), devices, areas, unitOfWork, _guard), _scope);
        _etlTasks = new ETLTasksController(
            new ETLTaskService(new ETLTaskRepository(_db), unitOfWork, _guard), _scope);
        _settings = new SettingsController(
            new SettingsService(new SystemSettingRepository(_db), unitOfWork, _guard), _scope);

        _roleService = new RoleService(
            new RoleRepository(_db), new UserRepository(_db), new CustomerRepository(_db), unitOfWork);
        var permissions = new PermissionService(new RoleRepository(_db), NullLogger<PermissionService>.Instance);
        _roles = new RolesController(_roleService, _scope, permissions);
    }

    /// <inheritdoc />
    public void Dispose() => _db.Dispose();

    private static T Attach<T>(T controller, System.Security.Claims.ClaimsPrincipal principal)
        where T : ControllerBase
    {
        controller.ControllerContext = ScopePrincipals.ForgedHeaderContext(principal);
        return controller;
    }

    private static int CodeOf<T>(ActionResult<ApiResponse<T>> result)
    {
        if (result.Result is OkObjectResult ok)
        {
            if (ok.Value is ApiResponse<T> wrapped) return wrapped.Code;
            if (ok.Value is ApiResponse<object> plain) return plain.Code;
        }
        if (result.Value is ApiResponse<T> direct) return direct.Code;
        return result.Value is ApiResponse<object> directPlain ? directPlain.Code : -1;
    }

    private static int PlainCodeOf(ActionResult<ApiResponse> result)
    {
        if (result.Result is OkObjectResult ok)
        {
            if (ok.Value is ApiResponse<object> wrapped) return wrapped.Code;
        }
        return result.Value is ApiResponse direct ? direct.Code : -1;
    }

    // ───────────────────────────── M2 ─────────────────────────────

    [Fact(DisplayName = "M2-01 admin 用集合外 AppCode 建协议配置 → 403 且零入库")]
    public async Task Admin_CreateProtocolConfig_OutsideGrant_Forbidden()
    {
        Attach(_protocolConfigs, ScopePrincipals.Multi(AdminId, CustomerOne, CustomerTwo)); // 伪造 claim 快照含客户2

        var response = await _protocolConfigs.CreateProtocolConfig(
            new CreateProtocolConfigRequest { Name = "越权协议", Type = "mqtt", AppCode = AppTwo });

        Assert.Equal(403, CodeOf(response));
        Assert.Equal(2, _db.ProtocolConfigs.IgnoreQueryFilters().Count());
    }

    [Fact(DisplayName = "M2-02 admin 未带归属建协议配置 → 403（appCode=null 不是跳过信号）")]
    public async Task Admin_CreateProtocolConfig_NoTarget_Forbidden()
    {
        Attach(_protocolConfigs, ScopePrincipals.Multi(AdminId, CustomerOne));

        var response = await _protocolConfigs.CreateProtocolConfig(
            new CreateProtocolConfigRequest { Name = "无主协议", Type = "mqtt" });

        Assert.Equal(403, CodeOf(response));
        Assert.Equal(2, _db.ProtocolConfigs.IgnoreQueryFilters().Count());
    }

    [Fact(DisplayName = "M2-03 admin 建授权客户协议配置 → 200，落库 AppCode 服务端派生（伪造头无效）")]
    public async Task Admin_CreateProtocolConfig_InGrant_DerivesAppCode()
    {
        // 请求体给出合法授权目标；同时携带伪造 X-App-Code 头，断言落库与之无关。
        Attach(_protocolConfigs, ScopePrincipals.Multi(AdminId, CustomerOne));

        var response = await _protocolConfigs.CreateProtocolConfig(new CreateProtocolConfigRequest
        {
            Name = "授权协议", Type = "mqtt", AppCode = AppOne
        });

        Assert.Equal(200, CodeOf(response));
        var created = _db.ProtocolConfigs.IgnoreQueryFilters().Single(c => c.Name == "授权协议");
        Assert.Equal(AppOne, created.AppCode);
    }

    [Fact(DisplayName = "M2-04 下级 Single 建配置沿用 claim，请求体伪造无效（零回归）")]
    public async Task Single_CreateProtocolConfig_UsesClaim()
    {
        Attach(_protocolConfigs, ScopePrincipals.Single(CustomerOne, AppOne));

        var response = await _protocolConfigs.CreateProtocolConfig(new CreateProtocolConfigRequest
        {
            Name = "下级协议", Type = "mqtt", AppCode = ScopePrincipals.ForgedAppCode
        });

        Assert.Equal(200, CodeOf(response));
        Assert.Equal(AppOne, _db.ProtocolConfigs.IgnoreQueryFilters().Single(c => c.Name == "下级协议").AppCode);
    }

    [Fact(DisplayName = "M2-05 超管建配置仍可指定目标 AppCode")]
    public async Task SuperAdmin_CreateProtocolConfig_ExplicitTarget()
    {
        Attach(_protocolConfigs, ScopePrincipals.All());

        var response = await _protocolConfigs.CreateProtocolConfig(new CreateProtocolConfigRequest
        {
            Name = "超管协议", Type = "mqtt", AppCode = AppTwo
        });

        Assert.Equal(200, CodeOf(response));
        Assert.Equal(AppTwo, _db.ProtocolConfigs.IgnoreQueryFilters().Single(c => c.Name == "超管协议").AppCode);
    }

    // ───────────────────────────── M3 ─────────────────────────────

    [Fact(DisplayName = "M3-01 admin 改他租户协议配置 → 403 且零写库")]
    public async Task Admin_UpdateForeignProtocolConfig_Forbidden()
    {
        Attach(_protocolConfigs, ScopePrincipals.Multi(AdminId, CustomerOne));

        var response = await _protocolConfigs.UpdateProtocolConfig(
            _foreignProtocolId, new UpdateProtocolConfigRequest { Name = "劫持" });

        Assert.Equal(403, CodeOf(response));
        Assert.Equal("他租户协议", _db.ProtocolConfigs.IgnoreQueryFilters().Single(c => c.Id == _foreignProtocolId).Name);
    }

    [Fact(DisplayName = "M3-02 admin 改协议配置 AppCode 到其他租户 → 403（租户劫持硬指标）")]
    public async Task Admin_ReassignProtocolConfigAppCode_Forbidden()
    {
        Attach(_protocolConfigs, ScopePrincipals.Multi(AdminId, CustomerOne));

        var response = await _protocolConfigs.UpdateProtocolConfig(
            _ownedProtocolId, new UpdateProtocolConfigRequest { Name = "本租户协议", AppCode = AppTwo });

        Assert.Equal(403, CodeOf(response));
        Assert.Equal(AppOne, _db.ProtocolConfigs.IgnoreQueryFilters().Single(c => c.Id == _ownedProtocolId).AppCode);
    }

    [Fact(DisplayName = "M3-03 admin 启停他租户协议 → 403（副作用前拦截）")]
    public async Task Admin_StartForeignProtocol_Forbidden()
    {
        Attach(_protocolConfigs, ScopePrincipals.Multi(AdminId, CustomerOne));

        var response = await _protocolConfigs.StartProtocol(_foreignProtocolId);

        Assert.Equal(403, PlainCodeOf(response));
        Assert.Equal("inactive", _db.ProtocolConfigs.IgnoreQueryFilters().Single(c => c.Id == _foreignProtocolId).Status);
    }

    [Fact(DisplayName = "M3-04 共享字典读放行：admin 可见 system 行")]
    public async Task Admin_ReadSharedDictionaryType_Allowed()
    {
        Attach(_dictionaries, ScopePrincipals.Multi(AdminId, CustomerOne));

        var response = await _dictionaries.GetDictionaryTypes();

        Assert.Equal(200, CodeOf(response));
        var payload = Assert.IsType<ApiResponse<PagedResponse<DictionaryTypeDto>>>(response.Value);
        Assert.Contains(payload.Data!.Items, d => d.Id == _sharedDictionaryTypeId);
    }

    [Fact(DisplayName = "M3-05 admin 写共享字典行 → 403；超管写 → 200（§3.5）")]
    public async Task SharedDictionaryType_WriteOnlySuperAdmin()
    {
        var request = new UpdateDictionaryTypeRequest { Name = "改名", IsActive = true };

        Attach(_dictionaries, ScopePrincipals.Multi(AdminId, CustomerOne));
        var forbidden = await _dictionaries.UpdateDictionaryType(_sharedDictionaryTypeId, request);
        Assert.Equal(403, CodeOf(forbidden));

        Attach(_dictionaries, ScopePrincipals.All());
        var allowed = await _dictionaries.UpdateDictionaryType(_sharedDictionaryTypeId, request);
        Assert.Equal(200, CodeOf(allowed));
        Assert.Equal("改名", _db.DictionaryTypeConfigs.IgnoreQueryFilters().Single(d => d.Id == _sharedDictionaryTypeId).Name);
    }

    [Fact(DisplayName = "M3-06 admin 改租户自建字典类型 → 200")]
    public async Task Admin_UpdateOwnedDictionaryType_Allowed()
    {
        Attach(_dictionaries, ScopePrincipals.Multi(AdminId, CustomerOne));

        var response = await _dictionaries.UpdateDictionaryType(
            _ownedDictionaryTypeId, new UpdateDictionaryTypeRequest { Name = "本租户改名", IsActive = true });

        Assert.Equal(200, CodeOf(response));
    }

    [Fact(DisplayName = "M3-07 admin 改他租户档案/规则/ETL/设置 → 全部 403（Service 软校验已 fail-closed）")]
    public async Task Admin_UpdateForeignConfigs_AllForbidden()
    {
        var admin = ScopePrincipals.Multi(AdminId, CustomerOne);

        Attach(_archives, admin);
        Assert.Equal(403, CodeOf(await _archives.UpdateArchive(
            _foreignArchiveId, new UpdateArchiveRequest { Name = "劫持档案" })));

        Attach(_dataRules, admin);
        Assert.Equal(403, CodeOf(await _dataRules.UpdateDataRule(
            _foreignRuleId, new UpdateDataRuleRequest { Name = "劫持规则" })));

        Attach(_etlTasks, admin);
        Assert.Equal(403, CodeOf(await _etlTasks.UpdateETLTask(
            _foreignTaskId, new UpdateETLTaskRequest { Name = "劫持任务" })));

        Attach(_settings, admin);
        Assert.Equal(403, CodeOf(await _settings.UpdateSetting(
            "tenant.key", new UpdateSettingRequest { Value = "hacked" })));

        Assert.Equal("v2", _db.SystemSettings.IgnoreQueryFilters().Single(s => s.Key == "tenant.key").Value);
    }

    [Fact(DisplayName = "M3-09 admin 改同名设置只命中本租户行，不误改共享行")]
    public async Task Admin_UpdateSetting_PrefersOwnTenantRow()
    {
        var admin = ScopePrincipals.Multi(AdminId, CustomerOne);
        Attach(_settings, admin);
        Assert.Equal(200, CodeOf(await _settings.CreateSetting(new CreateSettingRequest
        {
            Key = "dup.key", Value = "own", AppCode = AppOne
        })));
        _db.SystemSettings.Add(new SystemSetting { Key = "dup.key", Value = "shared", AppCode = null });
        _db.SaveChanges();

        var response = await _settings.UpdateSetting("dup.key", new UpdateSettingRequest { Value = "updated" });

        Assert.Equal(200, CodeOf(response));
        var rows = _db.SystemSettings.IgnoreQueryFilters().Where(s => s.Key == "dup.key").ToList();
        Assert.Equal("updated", rows.Single(s => s.AppCode == AppOne).Value);
        Assert.Equal("shared", rows.Single(s => s.AppCode == null).Value);
    }

    [Fact(DisplayName = "M3-08 admin 删除他租户 ETL 任务 → 403 且记录仍在")]
    public async Task Admin_DeleteForeignEtlTask_Forbidden()
    {
        Attach(_etlTasks, ScopePrincipals.Multi(AdminId, CustomerOne));

        var response = await _etlTasks.DeleteETLTask(_foreignTaskId);

        Assert.Equal(403, PlainCodeOf(response));
        Assert.NotNull(_db.EtlTasks.IgnoreQueryFilters().SingleOrDefault(t => t.Id == _foreignTaskId));
    }

    // ───────────────────────────── M10 ─────────────────────────────

    [Fact(DisplayName = "M10-01 admin 建角色未带 CustomerId → 400")]
    public async Task Admin_CreateRole_NoCustomerId_BadRequest()
    {
        Attach(_roles, ScopePrincipals.Multi(AdminId, CustomerOne));

        var response = await _roles.CreateRole(new CreateRoleRequest
        {
            Code = "R_NO_CUSTOMER", Name = "无归属角色", DataScope = "CUSTOM"
        });

        Assert.Equal(400, CodeOf(response));
        Assert.Empty(_db.Roles.IgnoreQueryFilters().Where(r => r.Code == "R_NO_CUSTOMER"));
    }

    [Fact(DisplayName = "M10-02 admin 用集合外客户建角色 → 403 且零入库")]
    public async Task Admin_CreateRole_OutsideGrant_Forbidden()
    {
        Attach(_roles, ScopePrincipals.Multi(AdminId, CustomerOne, CustomerTwo)); // 伪造 claim 含客户2

        var response = await _roles.CreateRole(new CreateRoleRequest
        {
            Code = "R_FOREIGN", Name = "越权角色", DataScope = "CUSTOM", CustomerId = CustomerTwo
        });

        Assert.Equal(403, CodeOf(response));
        Assert.Empty(_db.Roles.IgnoreQueryFilters().Where(r => r.Code == "R_FOREIGN"));
    }

    [Fact(DisplayName = "M10-03 admin 授予超出自身权限 → 403")]
    public async Task Admin_CreateRole_EscalatePermissions_Forbidden()
    {
        Attach(_roles, ScopePrincipals.Multi(AdminId, CustomerOne));

        var response = await _roles.CreateRole(new CreateRoleRequest
        {
            Code = "R_ESCALATE",
            Name = "提权角色",
            DataScope = "CUSTOM",
            CustomerId = CustomerOne,
            Permissions = new List<string> { Permissions.VIEW_ROLES, Permissions.DELETE_CUSTOMERS }
        });

        Assert.Equal(403, CodeOf(response));
        Assert.Empty(_db.Roles.IgnoreQueryFilters().Where(r => r.Code == "R_ESCALATE"));
    }

    [Fact(DisplayName = "M10-04 admin 合法建角色 → 200，AppCode 非 null 且由目标客户派生")]
    public async Task Admin_CreateRole_Legal_AppCodeDerived()
    {
        Attach(_roles, ScopePrincipals.Multi(AdminId, CustomerOne));

        var response = await _roles.CreateRole(new CreateRoleRequest
        {
            Code = "R_LEGAL",
            Name = "合法角色",
            DataScope = "CUSTOM",
            CustomerId = CustomerOne,
            Permissions = new List<string> { Permissions.VIEW_ROLES, Permissions.CREATE_ROLES }
        });

        Assert.Equal(200, CodeOf(response));
        var created = _db.Roles.IgnoreQueryFilters().Single(r => r.Code == "R_LEGAL");
        Assert.Equal(AppOne, created.AppCode);
        Assert.NotNull(created.AppCode);
    }

    [Fact(DisplayName = "M10-05 Service 二次防御：customerScopeVerified=false 拒绝写库")]
    public async Task Service_CreateRole_WithoutVerification_Throws()
    {
        await Assert.ThrowsAsync<ForbiddenAccessException>(() =>
            _roleService.CreateRoleAsync(
                new CreateRoleRequest { Code = "R_BYPASS", Name = "绕过", DataScope = "CUSTOM" },
                AppOne,
                customerScopeVerified: false));

        Assert.Empty(_db.Roles.IgnoreQueryFilters().Where(r => r.Code == "R_BYPASS"));
    }

    [Fact(DisplayName = "M10-06 下级 Single 建角色沿用 claim 原路径（零回归）")]
    public async Task Single_CreateRole_OriginalPath()
    {
        Attach(_roles, ScopePrincipals.Single(CustomerOne, AppOne));

        var response = await _roles.CreateRole(new CreateRoleRequest
        {
            Code = "R_SINGLE", Name = "下级角色", DataScope = "CUSTOM", CustomerId = CustomerTwo
        });

        Assert.Equal(200, CodeOf(response));
        Assert.Equal(AppOne, _db.Roles.IgnoreQueryFilters().Single(r => r.Code == "R_SINGLE").AppCode);
    }
}
