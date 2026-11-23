using System.Security.Claims;
using System.Text;
using IoTPlatform.Configuration;
using IoTPlatform.Controllers;
using IoTPlatform.Data;
using IoTPlatform.Data.Repositories.Implementations;
using IoTPlatform.Data.Repositories.Interfaces;
using IoTPlatform.Helpers;
using IoTPlatform.Models;
using IoTPlatform.Services;
using IoTPlatform.Services.Guards;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace IoTPlatform.AnSheng.Tests;

/// <summary>
/// M9-P0-0 请求头归属伪造专项测试（设计 §2.10 / §5.2）。
///
/// <para>【核心命题】<c>X-App-Code: customer_999</c> 完全前端可控，修复后<b>不得</b>被任何端点采信：
/// 三角色 + 匿名均构造该头，断言 (1) 落库 AppCode 只由服务端 scope 派生；(2) 越权返回 403；
/// (3) 越权删除时<b>物理文件未被删除</b>（原则 G：403 判定在任何删除动作之前）。</para>
///
/// <para>【零外部依赖】EF InMemory + 真实仓储/服务/控制器，上传根目录指向临时目录，
/// 物理文件断言直接落盘检查，不依赖真实数据库/MQTT。</para>
/// </summary>
public sealed class PatternM9_HeaderForgeryTests : IDisposable
{
    private const string OtherTenantAppCode = "customer_003";

    private readonly string _uploadRoot;
    private readonly AppDbContext _db;
    private readonly IUserCustomerRepository _grants;
    private readonly CustomerScopeService _scope;
    private readonly IConfiguration _configuration;
    private readonly FileStorageService _fileStorage;
    private readonly AttachmentsController _attachments;
    private readonly ContractsController _contracts;

    public PatternM9_HeaderForgeryTests()
    {
        _uploadRoot = Path.Combine(Path.GetTempPath(), "m9-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_uploadRoot);

        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"m9-{Guid.NewGuid():N}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

        for (long id = 1; id <= 4; id++)
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

        // 项目 100 归属客户 2（合同上传的归属来源）。
        _db.Projects.Add(new Project
        {
            Id = 100,
            CustomerId = 2,
            Name = "客户2项目",
            AppCode = "customer_002",
            Status = "planning",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        _db.SaveChanges();

        _grants = new UserCustomerRepository(_db);
        // admin(10) 授权客户 [1,2]，客户 3/4 为集合外。
        _grants.SyncAsync(10, new long[] { 1, 2 }, 1).GetAwaiter().GetResult();
        _db.ChangeTracker.Clear();

        _scope = new CustomerScopeService(
            _grants,
            new CustomerRepository(_db),
            new DeviceRepository(_db),
            new ProjectRepository(_db));

        _configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["FileStorage:UploadPath"] = _uploadRoot,
                ["FileStorage:MaxFileSizeMB"] = "50"
            })
            .Build();

        _fileStorage = new FileStorageService(
            _db,
            _configuration,
            new TestHostEnvironment(_uploadRoot),
            NullLogger<FileStorageService>.Instance,
            _scope);

        _attachments = new AttachmentsController(
            _db,
            _fileStorage,
            _configuration,
            NullLogger<AttachmentsController>.Instance,
            _scope);

        _contracts = new ContractsController(
            _db,
            _fileStorage,
            _configuration,
            NullLogger<ContractsController>.Instance,
            _scope,
            new DeviceAccessGuard(_db, _scope));
    }

    public void Dispose()
    {
        _db.Dispose();
        try
        {
            if (Directory.Exists(_uploadRoot))
            {
                Directory.Delete(_uploadRoot, recursive: true);
            }
        }
        catch (IOException)
        {
            // 临时目录清理失败不影响测试结论。
        }
    }

    // ───────────── Upload：落库 AppCode 与伪造头无关 ─────────────

    [Fact(DisplayName = "M9-01 下级 Single + 伪造头：落库 AppCode=自身 claim，头 customer_999 被彻底忽略")]
    public async Task Upload_Single_ForgedHeaderIgnored_UsesOwnClaim()
    {
        UseActor(_attachments, ScopePrincipals.Single(1, "customer_001"));

        var response = Unwrap(await _attachments.Upload(NewUploadRequest(customerId: 4 /* 越权目标，Single 必须忽略 */)));

        Assert.Equal(200, response.Code);
        var stored = await StoredAttachments();
        var record = Assert.Single(stored);
        Assert.Equal("customer_001", record.AppCode);
        Assert.DoesNotContain(ScopePrincipals.ForgedAppCode, record.AppCode ?? string.Empty);
        Assert.DoesNotContain(ScopePrincipals.ForgedAppCode, record.FileUrl ?? string.Empty);
        Assert.True(File.Exists(PhysicalPath(record)), "授权内上传应成功落盘");
    }

    [Fact(DisplayName = "M9-02 admin Multi + 伪造头 + 集合外 CustomerId=3 → 403，零附件零文件")]
    public async Task Upload_Multi_OutsideGrant_Returns403_NoFile()
    {
        UseActor(_attachments, ScopePrincipals.Multi(10, 1, 2));

        var response = Unwrap(await _attachments.Upload(NewUploadRequest(customerId: 3)));

        Assert.Equal(403, response.Code);
        Assert.Empty(await StoredAttachments());
        Assert.Empty(Directory.GetFiles(_uploadRoot, "*", SearchOption.AllDirectories));
    }

    [Fact(DisplayName = "M9-03 admin Multi + 伪造头 + 不指定 CustomerId → 403（不再回退 tenantContext/header）")]
    public async Task Upload_Multi_NoCustomerId_Returns403_NoFile()
    {
        UseActor(_attachments, ScopePrincipals.Multi(10, 1, 2));

        var response = Unwrap(await _attachments.Upload(NewUploadRequest(customerId: null)));

        Assert.Equal(403, response.Code);
        Assert.Empty(await StoredAttachments());
    }

    [Fact(DisplayName = "M9-04 admin Multi + 伪造头 + 授权内 CustomerId=2 → 200，AppCode 由服务端反查 customer_002")]
    public async Task Upload_Multi_InGrant_AppCodeServerDerived()
    {
        UseActor(_attachments, ScopePrincipals.Multi(10, 1, 2));

        var response = Unwrap(await _attachments.Upload(NewUploadRequest(customerId: 2)));

        Assert.Equal(200, response.Code);
        var record = Assert.Single(await StoredAttachments());
        Assert.Equal("customer_002", record.AppCode);
        Assert.DoesNotContain(ScopePrincipals.ForgedAppCode, record.AppCode ?? string.Empty);
        Assert.DoesNotContain(ScopePrincipals.ForgedAppCode, record.FileUrl ?? string.Empty);
    }

    [Fact(DisplayName = "M9-05 超管 All + 伪造头 + 未指定目标客户 → 400（超管亦不得靠头隐式归属）")]
    public async Task Upload_All_NoCustomerId_Returns400_NoFile()
    {
        UseActor(_attachments, ScopePrincipals.All());

        var response = Unwrap(await _attachments.Upload(NewUploadRequest(customerId: null)));

        Assert.Equal(400, response.Code);
        Assert.Empty(await StoredAttachments());
    }

    [Fact(DisplayName = "M9-06 超管 All + 伪造头 + 显式 CustomerId=4 → 200，AppCode=customer_004 与头无关")]
    public async Task Upload_All_ExplicitCustomer_AppCodeServerDerived()
    {
        UseActor(_attachments, ScopePrincipals.All());

        var response = Unwrap(await _attachments.Upload(NewUploadRequest(customerId: 4)));

        Assert.Equal(200, response.Code);
        var record = Assert.Single(await StoredAttachments());
        Assert.Equal("customer_004", record.AppCode);
        Assert.DoesNotContain(ScopePrincipals.ForgedAppCode, record.AppCode ?? string.Empty);
    }

    [Fact(DisplayName = "M9-07 匿名 + 伪造头 → 403 fail-closed，零附件零文件")]
    public async Task Upload_Anonymous_Returns403_NoFile()
    {
        UseActor(_attachments, ScopePrincipals.Anonymous());

        var response = Unwrap(await _attachments.Upload(NewUploadRequest(customerId: 2)));

        Assert.Equal(403, response.Code);
        Assert.Empty(await StoredAttachments());
        Assert.Empty(Directory.GetFiles(_uploadRoot, "*", SearchOption.AllDirectories));
    }

    [Fact(DisplayName = "M9-08 批量上传 + 伪造头 + 集合外目标 → 403，整批零落库零文件")]
    public async Task UploadBatch_Multi_OutsideGrant_Returns403_NoFile()
    {
        UseActor(_attachments, ScopePrincipals.Multi(10, 1, 2));
        var request = new UploadBatchAttachmentRequest
        {
            Module = "contracts",
            CustomerId = 3,
            Files = new List<IFormFile> { NewFormFile("a.txt"), NewFormFile("b.txt") }
        };

        var response = Unwrap(await _attachments.UploadBatch(request));

        Assert.Equal(403, response.Code);
        Assert.Empty(await StoredAttachments());
        Assert.Empty(Directory.GetFiles(_uploadRoot, "*", SearchOption.AllDirectories));
    }

    // ───────────── Delete：越权 403 且物理文件未被删除 ─────────────

    [Fact(DisplayName = "M9-09 admin 越权删除他人附件 → 403，物理文件与 DB 记录均仍在")]
    public async Task Delete_Multi_OutsideGrant_Forbidden_FileSurvives()
    {
        var record = await SeedAttachmentAsync("customer_001");
        var physicalPath = PhysicalPath(record);
        Assert.True(File.Exists(physicalPath), "前置：物理文件应存在");
        UseActor(_attachments, ScopePrincipals.Multi(10, 1, 2));

        // admin 授权集合改为 [2]，附件归属客户 1 → 越权
        await _grants.SyncAsync(10, new long[] { 2 }, 1);
        _db.ChangeTracker.Clear();

        var response = UnwrapPlain(await _attachments.Delete(record.Id));

        Assert.Equal(403, response.Code);
        Assert.True(File.Exists(physicalPath), "越权删除不得触碰物理文件");
        Assert.True(await _db.Attachments.AsNoTracking().IgnoreQueryFilters().AnyAsync(a => a.Id == record.Id),
            "越权删除不得删 DB 记录");
    }

    [Fact(DisplayName = "M9-10 下级越权删除他人附件 → 403，物理文件未被删除")]
    public async Task Delete_Single_OutsideOwnTenant_Forbidden_FileSurvives()
    {
        var record = await SeedAttachmentAsync("customer_002");
        var physicalPath = PhysicalPath(record);
        Assert.True(File.Exists(physicalPath));
        UseActor(_attachments, ScopePrincipals.Single(1, "customer_001"));

        var response = UnwrapPlain(await _attachments.Delete(record.Id));

        Assert.Equal(403, response.Code);
        Assert.True(File.Exists(physicalPath), "越权删除不得触碰物理文件");
        Assert.True(await _db.Attachments.AsNoTracking().IgnoreQueryFilters().AnyAsync(a => a.Id == record.Id));
    }

    [Fact(DisplayName = "M9-11 admin 授权内删除 → 200，物理文件与 DB 记录均删除")]
    public async Task Delete_Multi_InGrant_Succeeds_FileRemoved()
    {
        var record = await SeedAttachmentAsync("customer_001");
        var physicalPath = PhysicalPath(record);
        Assert.True(File.Exists(physicalPath));
        UseActor(_attachments, ScopePrincipals.Multi(10, 1, 2));

        var response = UnwrapPlain(await _attachments.Delete(record.Id));

        Assert.Equal(200, response.Code);
        Assert.False(File.Exists(physicalPath), "授权内删除应清理物理文件");
        Assert.False(await _db.Attachments.AsNoTracking().IgnoreQueryFilters().AnyAsync(a => a.Id == record.Id));
    }

    // ───────────── 合同：项目归属派生 AppCode + 越权删除无副作用 ─────────────

    [Fact(DisplayName = "M9-12 合同上传 + 伪造头：admin 仅能挂授权项目，AppCode 从项目归属派生")]
    public async Task UploadContract_Multi_InGrant_AppCodeFromProject()
    {
        UseActor(_contracts, ScopePrincipals.Multi(10, 1, 2));

        var response = Unwrap(await _contracts.UploadContract(NewUploadContractRequest(projectId: 100)));

        Assert.Equal(200, response.Code);
        var record = Assert.Single(await StoredAttachments());
        Assert.Equal("customer_002", record.AppCode); // AppCode 应来自项目归属客户，而非 header/tenantContext 空值
        Assert.DoesNotContain(ScopePrincipals.ForgedAppCode, record.AppCode ?? string.Empty);
    }

    [Fact(DisplayName = "M9-13 合同上传 + 伪造头：admin 越权挂他人项目 → 403，零附件零文件")]
    public async Task UploadContract_Multi_OutsideGrant_Forbidden_NoFile()
    {
        // admin 授权集合改为 [1]，项目 100 归属客户 2 → 越权
        await _grants.SyncAsync(10, new long[] { 1 }, 1);
        _db.ChangeTracker.Clear();
        UseActor(_contracts, ScopePrincipals.Multi(10, 1, 2));

        var response = Unwrap(await _contracts.UploadContract(NewUploadContractRequest(projectId: 100)));

        Assert.Equal(403, response.Code);
        Assert.Empty(await StoredAttachments());
        Assert.Empty(Directory.GetFiles(_uploadRoot, "*", SearchOption.AllDirectories));
    }

    [Fact(DisplayName = "M9-14 合同越权删除 → 403，物理文件未被删除")]
    public async Task DeleteContract_Multi_OutsideGrant_Forbidden_FileSurvives()
    {
        // 附件归属客户 3，超出 admin(10) 的授权集合 [1,2] → 必须 403。
        var record = await SeedAttachmentAsync(OtherTenantAppCode, module: "contracts");
        var physicalPath = PhysicalPath(record);
        Assert.True(File.Exists(physicalPath));
        UseActor(_contracts, ScopePrincipals.Multi(10, 1, 2));

        var response = UnwrapPlain(await _contracts.DeleteContract(record.Id));

        Assert.Equal(403, response.Code);
        Assert.True(File.Exists(physicalPath), "越权删除合同不得触碰物理文件");
        Assert.True(await _db.Attachments.AsNoTracking().IgnoreQueryFilters().AnyAsync(a => a.Id == record.Id));
    }

    // ───────────── 夹具 ─────────────

    private void UseActor(ControllerBase controller, ClaimsPrincipal principal) =>
        controller.ControllerContext = ScopePrincipals.ForgedHeaderContext(principal);

    private static UploadAttachmentRequest NewUploadRequest(long? customerId) => new()
    {
        File = NewFormFile("upload.txt"),
        Module = "contracts",
        CustomerId = customerId,
        Name = "测试附件"
    };

    private static UploadContractRequest NewUploadContractRequest(long projectId) => new()
    {
        File = NewFormFile("contract.txt"),
        ProjectId = projectId,
        Name = "测试合同",
        Type = "service"
    };

    private static IFormFile NewFormFile(string fileName)
    {
        var bytes = Encoding.UTF8.GetBytes("M9 header forgery test content");
        return new FormFile(new MemoryStream(bytes), 0, bytes.Length, "File", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = "text/plain"
        };
    }

    private async Task<Attachment> SeedAttachmentAsync(string appCode, string module = "contracts")
    {
        var record = await _fileStorage.UploadFileAsync(
            NewFormFile("seed.txt"), module, 100, "种子附件", 1, appCode, "seed");
        _db.ChangeTracker.Clear();
        return record;
    }

    private Task<List<Attachment>> StoredAttachments() => _db.Attachments
        .AsNoTracking()
        .IgnoreQueryFilters()
        .ToListAsync();

    private string PhysicalPath(Attachment record) =>
        Path.Combine(_uploadRoot, (record.FileUrl ?? string.Empty).TrimStart('/'));

    private static ApiResponse<T> Unwrap<T>(ActionResult<ApiResponse<T>> result) =>
        Assert.IsType<ApiResponse<T>>(result.Value ?? (result.Result as OkObjectResult)?.Value);

    /// <summary>
    /// 非泛型 ActionResult 可能承载 ApiResponse 或 ApiResponse&lt;object&gt;（Ok 包装），
    /// 与 MultiCustomerIsolationTests.PlainCode 同样兼容两种形态，只取机器可读 Code/Message。
    /// </summary>
    private static ApiResponse<object> UnwrapPlain(ActionResult<ApiResponse> result)
    {
        var value = result.Value ?? (result.Result as OkObjectResult)?.Value;
        return value switch
        {
            ApiResponse plain => new ApiResponse<object> { Code = plain.Code, Message = plain.Message },
            ApiResponse<object> generic => generic,
            var other => throw new Xunit.Sdk.XunitException($"未知响应形态：{other?.GetType().FullName ?? "null"}")
        };
    }

    /// <summary>最小 IWebHostEnvironment 桩：ContentTypeRoot 指向测试临时目录。</summary>
    private sealed class TestHostEnvironment : IWebHostEnvironment
    {
        public TestHostEnvironment(string contentRootPath)
        {
            ContentRootPath = contentRootPath;
        }

        public string ApplicationName { get; set; } = "M9Tests";
        public string EnvironmentName { get; set; } = "Development";
        public string ContentRootPath { get; set; }
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = string.Empty;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }
}
