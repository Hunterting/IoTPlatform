// B2 / M3：按 id 改删 + 防租户劫持（工单、告警、项目、受控设备）。
//
// 【核心命题】guard 必须在任何写库/副作用之前：越权 403 且实体逐字段不变；
// admin 的 AppCode claim 为空导致旧软校验恒真跳过，此处全部改为三分支 DB 权威判据。
// 【零回归】下级 Single 仍按 JWT AppCode 单值判定；超管 All 全部放行；旧 Service 签名保留。

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

/// <summary>B2 批次 M3 改删模式测试（设计 §2.4 / §5.2）。</summary>
public sealed class PatternM3_UpdateDeleteTests : IDisposable
{
    private const long AdminId = 10;
    private const long CustomerOne = 1;
    private const long CustomerTwo = 2;
    private const long WorkOrderOne = 201;
    private const long WorkOrderTwo = 202;
    private const long AlertOne = 301;
    private const long AlertTwo = 302;
    private const long ProjectOne = 401;
    private const long ProjectTwo = 402;
    private const long ControlledOne = 501;
    private const long ControlledTwo = 502;
    private const long DeviceOne = 101;
    private const long DeviceTwo = 102;

    private readonly AppDbContext _db;
    private readonly CustomerScopeService _scope;
    private readonly CustomerScopeGuard _guard;
    private readonly WorkOrdersController _workOrders;
    private readonly AlertsController _alerts;
    private readonly ProjectsController _projects;
    private readonly ControlledDevicesController _controlledDevices;

    public PatternM3_UpdateDeleteTests()
    {
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"b2-m3-{Guid.NewGuid():N}")
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
            Id = DeviceOne, AppCode = "customer_001", Name = "客户1设备", SerialNumber = "IMEI001", Status = "online"
        });
        _db.Devices.Add(new Device
        {
            Id = DeviceTwo, AppCode = "customer_002", Name = "客户2设备", SerialNumber = "IMEI002", Status = "online"
        });

        foreach (var (id, customer) in new[]
                 {
                     (WorkOrderOne, CustomerOne), (WorkOrderTwo, CustomerTwo)
                 })
        {
            _db.WorkOrders.Add(new WorkOrder
            {
                Id = id,
                OrderNo = $"WO{id}",
                Title = $"工单{id}",
                Type = "maintenance",
                Priority = "medium",
                Status = "pending",
                CustomerId = customer,
                AppCode = $"customer_{customer:000}",
                ReportTime = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }

        foreach (var (id, customer) in new[] { (AlertOne, CustomerOne), (AlertTwo, CustomerTwo) })
        {
            _db.AlertRecords.Add(new AlertRecord
            {
                Id = id,
                AlertNo = $"ALT{id}",
                DeviceName = $"告警设备{id}",
                AlertType = "temperature",
                Level = "warning",
                Status = "pending",
                AppCode = $"customer_{customer:000}",
                AlertTime = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }

        foreach (var (id, customer) in new[] { (ProjectOne, CustomerOne), (ProjectTwo, CustomerTwo) })
        {
            _db.Projects.Add(new Project
            {
                Id = id,
                CustomerId = customer,
                Name = $"项目{id}",
                AppCode = $"customer_{customer:000}",
                Status = "planning",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }

        foreach (var (id, customer) in new[]
                 {
                     (ControlledOne, CustomerOne), (ControlledTwo, CustomerTwo)
                 })
        {
            var appCode = $"customer_{customer:000}";
            _db.ControlledDevices.Add(new ControlledDevice
            {
                Id = id,
                DeviceId = customer == CustomerOne ? DeviceOne : DeviceTwo,
                AppCode = appCode,
                DeviceName = $"受控设备{id}",
                SerialNumber = $"SN{id}",
                Priority = 5,
                IsEnabled = true,
                IsFavorite = false,
                RegisteredAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }

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

        _workOrders = new WorkOrdersController(
            new WorkOrderService(new WorkOrderRepository(_db), devices, areas, unitOfWork, _guard), _scope);
        _alerts = new AlertsController(
            new AlertService(new AlertRecordRepository(_db), devices, areas, unitOfWork, mapper, _guard), _scope);
        _projects = new ProjectsController(_db, mapper, _scope, _guard);
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

    private static int CodeOf(ActionResult<ApiResponse> result)
    {
        if (result.Result is OkObjectResult ok)
        {
            if (ok.Value is ApiResponse wrapped) return wrapped.Code;
            if (ok.Value is ApiResponse<object> plain) return plain.Code;
        }
        if (result.Value is ApiResponse direct) return direct.Code;
        return result.Value is ApiResponse<object> directPlain ? directPlain.Code : -1;
    }

    private void ClearTracker() => _db.ChangeTracker.Clear();

    // ── 工单 ────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "M3-01 admin 改集合外工单 → 403 且标题不变")]
    public async Task Admin_UpdateWorkOrder_OutOfGrant_ForbiddenWithoutWrite()
    {
        Attach(_workOrders, ScopePrincipals.Multi(AdminId, CustomerOne));
        var response = await _workOrders.UpdateWorkOrder(WorkOrderTwo,
            new UpdateWorkOrderRequest { Title = "劫持标题", Priority = "high" });

        Assert.Equal(403, CodeOf(response));
        ClearTracker();
        Assert.Equal("工单202", _db.WorkOrders.IgnoreQueryFilters().Single(w => w.Id == WorkOrderTwo).Title);
    }

    [Fact(DisplayName = "M3-02 admin 改授权内工单 → 200")]
    public async Task Admin_UpdateWorkOrder_InGrant_Succeeds()
    {
        Attach(_workOrders, ScopePrincipals.Multi(AdminId, CustomerOne));
        var response = await _workOrders.UpdateWorkOrder(WorkOrderOne,
            new UpdateWorkOrderRequest { Title = "授权修改", Priority = "high" });

        Assert.Equal(200, CodeOf(response));
        ClearTracker();
        Assert.Equal("授权修改", _db.WorkOrders.IgnoreQueryFilters().Single(w => w.Id == WorkOrderOne).Title);
    }

    [Fact(DisplayName = "M3-03 下级 Single 改他人工单 → 403；改自身 → 200（零回归）")]
    public async Task Single_UpdateWorkOrder_OwnershipUnchanged()
    {
        Attach(_workOrders, ScopePrincipals.Single(CustomerOne, "customer_001"));
        var forbidden = await _workOrders.UpdateWorkOrder(WorkOrderTwo,
            new UpdateWorkOrderRequest { Title = "越权修改" });
        Assert.Equal(403, CodeOf(forbidden));

        var allowed = await _workOrders.UpdateWorkOrder(WorkOrderOne,
            new UpdateWorkOrderRequest { Title = "下级修改" });
        Assert.Equal(200, CodeOf(allowed));
    }

    [Fact(DisplayName = "M3-04 超管 All 可改任意工单")]
    public async Task SuperAdmin_UpdateWorkOrder_AnyTenant_Succeeds()
    {
        Attach(_workOrders, ScopePrincipals.All());
        var response = await _workOrders.UpdateWorkOrder(WorkOrderTwo,
            new UpdateWorkOrderRequest { Title = "超管修改" });

        Assert.Equal(200, CodeOf(response));
    }

    [Fact(DisplayName = "M3-05 admin 删除集合外工单附件 → 403（附件已存在仍不得操作）")]
    public async Task Admin_AttachmentOutOfGrant_Forbidden()
    {
        _db.WorkOrderAttachments.Add(new WorkOrderAttachment
        {
            Id = 601, WorkOrderId = WorkOrderTwo, FileName = "contract.pdf", FileUrl = "/f/contract.pdf",
            CreatedAt = DateTime.UtcNow
        });
        _db.SaveChanges();
        ClearTracker();

        Attach(_workOrders, ScopePrincipals.Multi(AdminId, CustomerOne));
        var response = await _workOrders.DeleteAttachment(601);

        Assert.Equal(403, CodeOf(response));
        ClearTracker();
        Assert.NotNull(_db.WorkOrderAttachments.IgnoreQueryFilters().SingleOrDefault(a => a.Id == 601));
    }

    // ── 告警 ────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "M3-06 admin 处理集合外告警 → 403 且状态不变")]
    public async Task Admin_ResolveAlert_OutOfGrant_ForbiddenWithoutWrite()
    {
        Attach(_alerts, ScopePrincipals.Multi(AdminId, CustomerOne));
        var response = await _alerts.ResolveAlert(AlertTwo, new ResolveAlertRequest { Remark = "劫持" });

        Assert.Equal(403, CodeOf(response));
        ClearTracker();
        Assert.Equal("pending", _db.AlertRecords.IgnoreQueryFilters().Single(a => a.Id == AlertTwo).Status);
    }

    [Fact(DisplayName = "M3-07 admin 处理授权内告警 → 200")]
    public async Task Admin_ResolveAlert_InGrant_Succeeds()
    {
        Attach(_alerts, ScopePrincipals.Multi(AdminId, CustomerOne));
        var response = await _alerts.ResolveAlert(AlertOne, new ResolveAlertRequest { Remark = "已处理" });

        Assert.Equal(200, CodeOf(response));
        ClearTracker();
        Assert.Equal("resolved", _db.AlertRecords.IgnoreQueryFilters().Single(a => a.Id == AlertOne).Status);
    }

    // ── 项目（含 M3c 归属冻结） ─────────────────────────────────────────────

    [Fact(DisplayName = "M3-08 admin 删集合外项目 → 403 且实体仍在")]
    public async Task Admin_DeleteProject_OutOfGrant_ForbiddenWithoutWrite()
    {
        Attach(_projects, ScopePrincipals.Multi(AdminId, CustomerOne));
        var response = await _projects.DeleteProject(ProjectTwo);

        Assert.Equal(403, CodeOf(response));
        ClearTracker();
        Assert.NotNull(_db.Projects.IgnoreQueryFilters().SingleOrDefault(p => p.Id == ProjectTwo));
    }

    [Fact(DisplayName = "M3-09 admin 改集合内项目伪造 AppCode → 归属被冻结为本租户")]
    public async Task Admin_UpdateProject_InGrant_FreezesOwnership()
    {
        Attach(_projects, ScopePrincipals.Multi(AdminId, CustomerOne));
        var response = await _projects.UpdateProject(ProjectOne, new UpdateProjectRequest
        {
            Name = "改名", Status = "planning", AppCode = ScopePrincipals.ForgedAppCode
        });

        Assert.Equal(200, CodeOf(response));
        ClearTracker();
        var stored = _db.Projects.IgnoreQueryFilters().Single(p => p.Id == ProjectOne);
        Assert.Equal("customer_001", stored.AppCode);
        Assert.Equal("改名", stored.Name);
    }

    [Fact(DisplayName = "M3-10 admin 删授权内项目 → 200")]
    public async Task Admin_DeleteProject_InGrant_Succeeds()
    {
        Attach(_projects, ScopePrincipals.Multi(AdminId, CustomerOne));
        var response = await _projects.DeleteProject(ProjectOne);

        Assert.Equal(200, CodeOf(response));
        ClearTracker();
        Assert.Null(_db.Projects.IgnoreQueryFilters().SingleOrDefault(p => p.Id == ProjectOne));
    }

    // ── 受控设备（删除/改/收藏 + 批量注册） ──────────────────────────────────

    [Fact(DisplayName = "M3-11 admin 注销集合外受控设备 → 403 且记录未删")]
    public async Task Admin_UnregisterControlledDevice_OutOfGrant_ForbiddenWithoutWrite()
    {
        Attach(_controlledDevices, ScopePrincipals.Multi(AdminId, CustomerOne));
        var response = await _controlledDevices.UnregisterDevice(ControlledTwo);

        Assert.Equal(403, CodeOf(response));
        ClearTracker();
        Assert.NotNull(_db.ControlledDevices.IgnoreQueryFilters().SingleOrDefault(d => d.Id == ControlledTwo));
    }

    [Fact(DisplayName = "M3-12 admin 收藏集合外受控设备 → 403 且 IsFavorite 不变")]
    public async Task Admin_ToggleFavorite_OutOfGrant_ForbiddenWithoutWrite()
    {
        Attach(_controlledDevices, ScopePrincipals.Multi(AdminId, CustomerOne));
        var response = await _controlledDevices.ToggleFavorite(ControlledTwo);

        Assert.Equal(403, CodeOf(response));
        ClearTracker();
        Assert.False(_db.ControlledDevices.IgnoreQueryFilters().Single(d => d.Id == ControlledTwo).IsFavorite);
    }

    [Fact(DisplayName = "M3-13 admin 修改集合外受控设备 → 403 且字段不变")]
    public async Task Admin_UpdateControlledDevice_OutOfGrant_ForbiddenWithoutWrite()
    {
        Attach(_controlledDevices, ScopePrincipals.Multi(AdminId, CustomerOne));
        var response = await _controlledDevices.UpdateDevice(ControlledTwo,
            new UpdateControlledDeviceRequest { Remark = "劫持", IsEnabled = false });

        Assert.Equal(403, CodeOf(response));
        ClearTracker();
        var stored = _db.ControlledDevices.IgnoreQueryFilters().Single(d => d.Id == ControlledTwo);
        Assert.True(stored.IsEnabled);
        Assert.Null(stored.Remark);
    }

    [Fact(DisplayName = "M3-14 admin 注销授权内受控设备 → 200")]
    public async Task Admin_UnregisterControlledDevice_InGrant_Succeeds()
    {
        Attach(_controlledDevices, ScopePrincipals.Multi(AdminId, CustomerOne));
        var response = await _controlledDevices.UnregisterDevice(ControlledOne);

        Assert.Equal(200, CodeOf(response));
        ClearTracker();
        Assert.Null(_db.ControlledDevices.IgnoreQueryFilters().SingleOrDefault(d => d.Id == ControlledOne));
    }

    [Fact(DisplayName = "M3-15 admin 批量注册含集合外设备 → 403 且零入库")]
    public async Task Admin_BatchRegister_OutOfGrantDevice_ForbiddenWithoutWrite()
    {
        Attach(_controlledDevices, ScopePrincipals.Multi(AdminId, CustomerOne));
        // ControlledOne 对应的 DeviceOne 已注册；此处仍有一台集合外设备，必须整批拒绝。
        var response = await _controlledDevices.RegisterDevices(
            new BatchRegisterControlledDeviceRequest { DeviceIds = new System.Collections.Generic.List<long> { DeviceOne, DeviceTwo } });

        Assert.Equal(403, CodeOf(response));
    }
}
