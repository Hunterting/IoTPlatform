using System.Threading;
using System.Threading.Tasks;
using IoTPlatform.Configuration;
using IoTPlatform.Data;
using IoTPlatform.Infrastructure.Protocol.AnSheng;
using IoTPlatform.Models;
using IoTPlatform.Services;
using IoTPlatform.Services.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IoTPlatform.Services.AnShengEventHandlers;

/// <summary>
/// <c>recv485</c>（RS485 透传上行）事件 Handler（决策 2）。
///
/// 业务动作：
///   · <see cref="AnShengEventOptions.PersistRecv485"/> 控制是否写事件溯源表——
///     生产默认 <c>false</c>（485 高频，D4 §372 顾虑），测试环境 <c>true</c> 便于断言；
///   · 不论是否写事件表，数据都经 Normalizer → <see cref="IDataCollectionService"/>
///     → <c>DeviceDataRecord.SensorData</c> 无损承载十六进制帧（<c>rs485_hex</c> 等）。
///
/// 不变更既有设计：485 专用数据表结构未定义，登记为待办 W4。
/// </summary>
public sealed class Recv485EventHandler : AnShengEventHandlerBase
{
    private readonly AnShengEventOptions _options;

    /// <summary>构造 Handler。</summary>
    public Recv485EventHandler(
        AnShengDataNormalizer normalizer,
        IDataCollectionService collector,
        AppDbContext db,
        IOptions<AnShengEventOptions> options,
        ILogger<Recv485EventHandler> logger)
        : base(normalizer, collector, db, logger)
    {
        _options = options?.Value ?? new AnShengEventOptions();
    }

    /// <inheritdoc />
    public override string Method => "recv485";

    /// <inheritdoc />
    protected override Task<AnShengEventOutcome> OnHandleAsync(
        AnShengUplinkContext ctx, CancellationToken cancellationToken)
    {
        // 决策 2：高频数据默认不写事件表，但一定投递规则引擎（数据无损落 SensorData）。
        return Task.FromResult(new AnShengEventOutcome
        {
            PersistEvent = _options.PersistRecv485,
            DispatchToRules = true,
            Severity = AnShengEventSeverity.Info,
        });
    }
}
