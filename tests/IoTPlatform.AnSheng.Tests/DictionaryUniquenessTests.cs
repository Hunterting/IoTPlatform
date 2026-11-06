using System;
using System.Threading.Tasks;
using IoTPlatform.Data;
using IoTPlatform.Data.Repositories.Implementations;
using IoTPlatform.DTOs.Requests;
using IoTPlatform.Models;
using IoTPlatform.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace IoTPlatform.AnSheng.Tests;

/// <summary>
/// 针对「根治字典管理遗留 1/2」改动的针对性回归：
///  - 类型 Code 全局唯一（跨所有租户 + system 不得复用）；
///  - 字典项 (Type, Code) 跨「本租户 + system」不得与平台项重复产生双行；
///  - LogRepository.LogOperationAsync 对 Detail 超长截断兜底。
/// 使用 EF Core InMemory，不依赖 MySQL，不污染测试库。
/// </summary>
public class DictionaryUniquenessTests
{
    private static AppDbContext CreateInMemoryContext(string name)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(name)
            .Options;
        return new AppDbContext(options);
    }

    private static DictionaryService BuildService(AppDbContext ctx)
    {
        var repo = new DictionaryRepository(ctx);
        var uow = new UnitOfWork(ctx);
        return new DictionaryService(repo, uow);
    }

    [Fact]
    public async Task CreateDictionaryType_AsTenant_WithSameCodeAsSystemType_ThrowsInvalidOpWithExistsMessage()
    {
        var ctx = CreateInMemoryContext($"dict-type-{Guid.NewGuid():N}");
        ctx.DictionaryTypeConfigs.Add(new DictionaryTypeConfig
        {
            Code = "GENDER",
            Name = "性别",
            AppCode = "system",
            IsActive = true,
            SortOrder = 1,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();

        var service = BuildService(ctx);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateDictionaryTypeAsync(new CreateDictionaryTypeRequest
            {
                Code = "GENDER",
                Name = "性别副本"
            }, appCode: "tenantA"));

        Assert.Contains("已存在", ex.Message);
    }

    [Fact]
    public async Task CreateDictionaryType_AsTenant_WithUniqueCode_Succeeds()
    {
        var ctx = CreateInMemoryContext($"dict-type-ok-{Guid.NewGuid():N}");
        var service = BuildService(ctx);
        var dto = await service.CreateDictionaryTypeAsync(new CreateDictionaryTypeRequest
        {
            Code = "ORDER_STATUS",
            Name = "订单状态"
        }, appCode: "tenantA");

        Assert.Equal("ORDER_STATUS", dto.Code);
        Assert.Equal("tenantA", dto.AppCode);
    }

    [Fact]
    public async Task CreateDictionaryItem_AsTenant_UnderSharedType_WithSameCodeAsSystemItem_ThrowsInvalidOpWithExistsMessage()
    {
        var ctx = CreateInMemoryContext($"dict-item-{Guid.NewGuid():N}");
        ctx.DictionaryTypeConfigs.Add(new DictionaryTypeConfig
        {
            Code = "GENDER",
            Name = "性别",
            AppCode = "system",
            IsActive = true,
            SortOrder = 1,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        ctx.DictionaryItems.Add(new DictionaryItem
        {
            Type = "GENDER",
            Code = "M",
            Name = "男",
            Status = "active",
            AppCode = "system",
            Sort = 1,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();

        var service = BuildService(ctx);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateDictionaryItemAsync(new CreateDictionaryItemRequest
            {
                Type = "GENDER",
                Code = "M",
                Name = "男副本"
            }, appCode: "tenantA"));

        Assert.Contains("已存在", ex.Message);
    }

    [Fact]
    public async Task CreateDictionaryItem_AsTenant_WithUniqueCodeUnderSharedType_Succeeds()
    {
        var ctx = CreateInMemoryContext($"dict-item-ok-{Guid.NewGuid():N}");
        ctx.DictionaryTypeConfigs.Add(new DictionaryTypeConfig
        {
            Code = "GENDER",
            Name = "性别",
            AppCode = "system",
            IsActive = true,
            SortOrder = 1,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        ctx.DictionaryItems.Add(new DictionaryItem
        {
            Type = "GENDER",
            Code = "M",
            Name = "男",
            Status = "active",
            AppCode = "system",
            Sort = 1,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();

        var service = BuildService(ctx);
        var dto = await service.CreateDictionaryItemAsync(new CreateDictionaryItemRequest
        {
            Type = "GENDER",
            Code = "F",
            Name = "女"
        }, appCode: "tenantA");

        Assert.Equal("F", dto.Code);
        Assert.Equal("tenantA", dto.AppCode);
    }

    [Fact]
    public async Task CreateDictionaryItem_TenantItemDoesNotCollideWithAnotherTenantItem()
    {
        var ctx = CreateInMemoryContext($"dict-item-cross-{Guid.NewGuid():N}");
        ctx.DictionaryTypeConfigs.Add(new DictionaryTypeConfig
        {
            Code = "GENDER",
            Name = "性别",
            AppCode = "system",
            IsActive = true,
            SortOrder = 1,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        ctx.DictionaryItems.Add(new DictionaryItem
        {
            Type = "GENDER",
            Code = "X",
            Name = "租户B项",
            Status = "active",
            AppCode = "tenantB",
            Sort = 1,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();

        var service = BuildService(ctx);
        var dto = await service.CreateDictionaryItemAsync(new CreateDictionaryItemRequest
        {
            Type = "GENDER",
            Code = "X",
            Name = "租户A项"
        }, appCode: "tenantA");

        Assert.Equal("tenantA", dto.AppCode);
    }
}

public class LogRepositoryTruncationTests
{
    private static AppDbContext CreateInMemoryContext(string name)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(name)
            .Options;
        return new AppDbContext(options);
    }

    [Fact]
    public async Task LogOperation_DetailExceeding2000_TruncatedTo2000EndingWithEllipsis()
    {
        var ctx = CreateInMemoryContext($"log-trunc-{Guid.NewGuid():N}");
        var repo = new LogRepository(ctx);
        var longDetail = new string('A', 5000);
        await repo.LogOperationAsync(1, "tester", "admin", "Module", "Action", detail: longDetail);

        var saved = await ctx.OperationLogs.SingleAsync();
        Assert.Equal(2000, saved.Detail!.Length);
        Assert.EndsWith("...", saved.Detail);
        Assert.StartsWith(new string('A', 1997), saved.Detail);
    }

    [Fact]
    public async Task LogOperation_DetailWithinLimit_Unchanged()
    {
        var ctx = CreateInMemoryContext($"log-ok-{Guid.NewGuid():N}");
        var repo = new LogRepository(ctx);
        var detail = new string('B', 1000);
        await repo.LogOperationAsync(1, "tester", "admin", "Module", "Action", detail: detail);

        var saved = await ctx.OperationLogs.SingleAsync();
        Assert.Equal(1000, saved.Detail!.Length);
        Assert.Equal(detail, saved.Detail);
    }
}
