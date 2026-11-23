// B2 / M1：带 CustomerId 的创建授权收口（工单、告警、项目、区域、受控设备注册）。
//
// 【核心命题】admin 自身 AppCode/CustomerId claim 为空，任何 `!string.IsNullOrEmpty(appCode) && ...`
// 式软校验恒假跳过。修复后 admin 必须在写库之前通过 DB 权威授权集合校验目标客户，
// 归属 AppCode 由服务端从客户/设备反查，绝不采信请求体或 X-App-Code 头。
// 【零回归】超管 All 与下级 Single 沿用原路径；旧 Service 签名保留。

using System;
using System.Linq;
using System.Threading.Tasks;
using AutoMapper;
using IoTPlatform.Configuration;
using IoTPlatform.Controllers;
using IoTPlatform.Data;
using IoTPlatform.Data.Repositories.Implementations;
using IoTPlatform.DTOs.Profiles;
using IoTPlatform.DTOs.Requests;
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
/// B2 批次 M1 创建模式测试（设计 §2.2 / §5.2）。
/// 装配真实 InMemory DbContext + 真实范围服务/守卫/仓储/服务/控制器，只有日志器是替身。
/// </summary>
public sealed class PatternM1_CreateWithCustomerIdTests : IDisposable
{
    private const long AdminId = 10;
    private const long CustomerOne = 1;
    private const long CustomerTwo = 2;
    private const long DeviceOne = 101;
    private const long DeviceTwo = 102;

    private readonly AppDbContext _db;
    private readonly CustomerScopeService _scope;
    private readonly CustomerScopeGuard _guard;
    private readonly WorkOrdersController _workOrders;
    private readonly AlertsController _alerts;
    private readonly ProjectsController _projects;
    private readonly AreasController _areas;
    private readonly ControlledDevicesController _controlledDevices;
    private readonly WorkOrderService _workOrderService;

    public PatternM1_CreateWithCustomerIdTests()
    {
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"b2-m1-{Guid.NewGuid():N}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

        foreach (var id in new[] { CustomerOne, CustomerTwo })
        {
            _db.Customers.Add(new Customer
            {
                Id = id,
                Name = $"客户{id}",
                Code = $"C{id:000}",
                AppCode = $"customer_{id:000}",
                Status = "active",
                MaxDeviceCount = 100,
                MaxUserCount = 100,
                MaxAreaCount = 100,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }

        _db.Devices.Add(new Device
        {
            Id = DeviceOne,
            AppCode = "customer_001",
            Name = "客户1设备",
            SerialNumber = "IMEI001",
            Status = "online"
        });
        _db.Devices.Add(new Device
        {
            Id = DeviceTwo,
            AppCode = "customer_002",
            Name = "客户2设备",
            SerialNumber = "IMEI002",
            Status = "online"
        });
        _db.SaveChanges();

        var grants = new UserCustomerRepository(_db);
        grants.SyncAsync(AdminId, new[] { CustomerOne }, 1).GetAwaiter().GetResult();
        _db.SaveChanges();
        _db.ChangeTracker.Clear();

        _scope = new CustomerScopeService(
            grants, new CustomerRepository(_db), new DeviceRepository(_db), new ProjectRepository(_db));
        _guard = new CustomerScopeGuard(_scope);

        var mapper = new MapperConfiguration(cfg => cfg.AddProfile<MappingProfile>()).CreateMapper();
        var unitOfWork = new UnitOfWork(_db);
        var devices = new DeviceRepository(_db);
        var areas = new AreaRepository(_db);

        _workOrderService = new WorkOrderService(
            new WorkOrderRepository(_db), devices, areas, unitOfWork, _guard);
        _workOrders = new WorkOrdersController(_workOrderService, _scope);
        _alerts = new AlertsController(
            new AlertService(new AlertRecordRepository(_db), devices, areas, unitOfWork, mapper, _guard),
            _scope);
        _projects = new ProjectsController(_db, mapper, _scope, _guard);
        _areas = new AreasController(
            new AreaService(areas, new CustomerRepository(_db), devices, unitOfWork), _scope);
        _controlledDevices = new ControlledDevicesController(
            new ControlledDeviceService(_db, NullLogger<ControlledDeviceService>.Instance, _guard), _scope);
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
            // 部分端点返回非泛型 ApiResponse（继承 ApiResponse<object>）。
            if (ok.Value is ApiResponse<object> plain) return plain.Code;
        }
        if (result.Value is ApiResponse<T> direct) return direct.Code;
        return result.Value is ApiResponse<object> directPlain ? directPlain.Code : -1;
    }

    // ── 工单 ────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "M1-01 admin 用集合外 CustomerId 建工单 → 403 且零入库")]
    public async Task Admin_CreateWorkOrder_OutOfGrant_ForbiddenWithoutWrite()
    {
        Attach(_workOrders, ScopePrincipals.Multi(AdminId, CustomerOne, CustomerTwo)); // 伪造 claim 快照
        var request = new CreateWorkOrderRequest { Title = "越权工单", CustomerId = CustomerTwo };

        var response = await _workOrders.CreateWorkOrder(request);

        Assert.Equal(403, CodeOf(response));
        Assert.Empty(_db.WorkOrders.IgnoreQueryFilters().ToList());
    }

    [Fact(DisplayName = "M1-02 admin 用授权客户建工单 → 200 且 AppCode 服务端派生")]
    public async Task Admin_CreateWorkOrder_InGrant_DerivesAppCode()
    {
        Attach(_workOrders, ScopePrincipals.Multi(AdminId, CustomerOne));
        var request = new CreateWorkOrderRequest
        {
            Title = "授权工单",
            CustomerId = CustomerOne,
            AppCode = ScopePrincipals.ForgedAppCode // 请求体伪造必须被服务端覆盖
        };

        var response = await _workOrders.CreateWorkOrder(request);

        Assert.Equal(200, CodeOf(response));
        var stored = Assert.Single(_db.WorkOrders.IgnoreQueryFilters().ToList());
        Assert.Equal("customer_001", stored.AppCode);
        Assert.Equal(CustomerOne, stored.CustomerId);
    }

    [Fact(DisplayName = "M1-03 下级 Single 建工单走原路径，零回归")]
    public async Task Single_CreateWorkOrder_OriginalPath()
    {
        Attach(_workOrders, ScopePrincipals.Single(CustomerOne, "customer_001"));
        var request = new CreateWorkOrderRequest
        {
            Title = "下级工单",
            CustomerId = CustomerOne,
            AppCode = "customer_002" // 旧逻辑：TenantAppCode 覆盖 request.AppCode
        };

        var response = await _workOrders.CreateWorkOrder(request);

        Assert.Equal(200, CodeOf(response));
        var stored = Assert.Single(_db.WorkOrders.IgnoreQueryFilters().ToList());
        Assert.Equal("customer_001", stored.AppCode);
    }

    [Fact(DisplayName = "M1-04 超管 All 建工单走原路径，可代任意租户")]
    public async Task SuperAdmin_CreateWorkOrder_OriginalPath()
    {
        Attach(_workOrders, ScopePrincipals.All());
        var request = new CreateWorkOrderRequest
        {
            Title = "超管工单",
            CustomerId = CustomerTwo,
            AppCode = "customer_002"
        };

        var response = await _workOrders.CreateWorkOrder(request);

        Assert.Equal(200, CodeOf(response));
        Assert.Equal("customer_002", Assert.Single(_db.WorkOrders.IgnoreQueryFilters().ToList()).AppCode);
    }

    [Fact(DisplayName = "M1-05 Service 二次防御：customerScopeVerified=false 一律拒绝")]
    public async Task Service_CreateWorkOrder_WithoutVerification_Throws()
    {
        await Assert.ThrowsAsync<ForbiddenAccessException>(() =>
            _workOrderService.CreateWorkOrderAsync(
                new CreateWorkOrderRequest { Title = "绕过", CustomerId = CustomerOne },
                "customer_001",
                customerScopeVerified: false));
        Assert.Empty(_db.WorkOrders.IgnoreQueryFilters().ToList());
    }

    // ── 告警 ────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "M1-06 admin 用集合外 CustomerId 建告警 → 403 且零入库")]
    public async Task Admin_CreateAlert_OutOfGrant_ForbiddenWithoutWrite()
    {
        Attach(_alerts, ScopePrincipals.Multi(AdminId, CustomerOne));
        var request = new CreateAlertRequest
        {
            DeviceName = "越权告警", AlertType = "temperature", Level = "warning", CustomerId = CustomerTwo
        };

        var response = await _alerts.CreateAlert(request);

        Assert.Equal(403, CodeOf(response));
        Assert.Empty(_db.AlertRecords.IgnoreQueryFilters().ToList());
    }

    [Fact(DisplayName = "M1-07 admin 建告警未带 CustomerId → 400")]
    public async Task Admin_CreateAlert_MissingCustomerId_BadRequest()
    {
        Attach(_alerts, ScopePrincipals.Multi(AdminId, CustomerOne));

        var response = await _alerts.CreateAlert(new CreateAlertRequest
        {
            DeviceName = "无归属告警", AlertType = "temperature", Level = "warning"
        });

        Assert.Equal(400, CodeOf(response));
        Assert.Empty(_db.AlertRecords.IgnoreQueryFilters().ToList());
    }

    [Fact(DisplayName = "M1-08 admin 建授权客户告警 → 200 且 AppCode 服务端派生")]
    public async Task Admin_CreateAlert_InGrant_DerivesAppCode()
    {
        Attach(_alerts, ScopePrincipals.Multi(AdminId, CustomerOne));
        var request = new CreateAlertRequest
        {
            DeviceName = "授权告警",
            AlertType = "temperature",
            Level = "warning",
            CustomerId = CustomerOne,
            DeviceId = DeviceTwo, // 设备属于客户2：不能按设备反查越权落库
            AppCode = ScopePrincipals.ForgedAppCode
        };

        var response = await _alerts.CreateAlert(request);

        Assert.Equal(200, CodeOf(response));
        Assert.Equal("customer_001", Assert.Single(_db.AlertRecords.IgnoreQueryFilters().ToList()).AppCode);
    }

    // ── 项目 / 区域 ──────────────────────────────────────────────────────────

    [Fact(DisplayName = "M1-09 admin 用集合外 CustomerId 建项目 → 403 且零入库")]
    public async Task Admin_CreateProject_OutOfGrant_ForbiddenWithoutWrite()
    {
        Attach(_projects, ScopePrincipals.Multi(AdminId, CustomerOne));

        var response = await _projects.CreateProject(new CreateProjectRequest
        {
            CustomerId = CustomerTwo, Name = "越权项目", Status = "planning"
        });

        Assert.Equal(403, CodeOf(response));
        Assert.Empty(_db.Projects.IgnoreQueryFilters().ToList());
    }

    [Fact(DisplayName = "M1-10 admin 建授权客户项目 → 200 且 AppCode 服务端派生")]
    public async Task Admin_CreateProject_InGrant_DerivesAppCode()
    {
        Attach(_projects, ScopePrincipals.Multi(AdminId, CustomerOne));

        var response = await _projects.CreateProject(new CreateProjectRequest
        {
            CustomerId = CustomerOne,
            Name = "授权项目",
            Status = "planning",
            AppCode = ScopePrincipals.ForgedAppCode
        });

        Assert.Equal(200, CodeOf(response));
        var stored = Assert.Single(_db.Projects.IgnoreQueryFilters().ToList());
        Assert.Equal("customer_001", stored.AppCode);
        Assert.Equal(CustomerOne, stored.CustomerId);
    }

    [Fact(DisplayName = "M1-11 admin CreateArea 遗留缺口：越集客户 403，授权客户 200")]
    public async Task Admin_CreateArea_OutOfGrantForbidden_InGrantDerivedAppCode()
    {
        Attach(_areas, ScopePrincipals.Multi(AdminId, CustomerOne));

        var forbidden = await _areas.CreateArea(new CreateAreaRequest
        {
            Name = "越权区域", Type = "level1", CustomerId = CustomerTwo
        });
        Assert.Equal(403, CodeOf(forbidden));

        var allowed = await _areas.CreateArea(new CreateAreaRequest
        {
            Name = "授权区域", Type = "level1", CustomerId = CustomerOne,
            AppCode = ScopePrincipals.ForgedAppCode
        });
        Assert.Equal(200, CodeOf(allowed));
        var stored = Assert.Single(_db.Areas.IgnoreQueryFilters().ToList());
        Assert.Equal("customer_001", stored.AppCode);
    }

    // ── 受控设备注册 ─────────────────────────────────────────────────────────

    [Fact(DisplayName = "M1-12 admin 注册集合外设备 → 403 且零入库")]
    public async Task Admin_RegisterControlledDevice_OutOfGrant_Forbidden()
    {
        Attach(_controlledDevices, ScopePrincipals.Multi(AdminId, CustomerOne));

        var response = await _controlledDevices.RegisterDevice(
            new RegisterControlledDeviceRequest { DeviceId = DeviceTwo });

        Assert.Equal(403, CodeOf(response));
        Assert.Empty(_db.ControlledDevices.IgnoreQueryFilters().ToList());
    }

    [Fact(DisplayName = "M1-13 admin 注册授权设备 → 200 且落库 AppCode 非 null（设备真实值）")]
    public async Task Admin_RegisterControlledDevice_InGrant_NonNullAppCode()
    {
        Attach(_controlledDevices, ScopePrincipals.Multi(AdminId, CustomerOne));

        var response = await _controlledDevices.RegisterDevice(
            new RegisterControlledDeviceRequest { DeviceId = DeviceOne });

        Assert.Equal(200, CodeOf(response));
        var stored = Assert.Single(_db.ControlledDevices.IgnoreQueryFilters().ToList());
        Assert.Equal("customer_001", stored.AppCode);
        Assert.NotNull(stored.AppCode);
    }
}
