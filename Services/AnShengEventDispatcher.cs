using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IoTPlatform.Infrastructure.Protocol.AnSheng;
using IoTPlatform.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace IoTPlatform.Services;

/// <summary>
/// 安圣事件分发器 —— 按 <see cref="IAnShengEventHandler.Method"/> 建 O(1) 索引，
/// 把一条 Event 报文派发给对应 Handler。
///
/// 【为什么不是线性责任链遍历】
///   7 个事件类型固定且互相独立，按 method 字典索引派发是 O(1)，
///   比「逐个 Handler.TryHandle」更符合本场景（设计 §1.3）。
///
/// 【启动期自检（设计 §3.4 / §9.4）】
///   构造时校验「硬 ∪ 软 共 7 个方法」是否全部有 Handler 覆盖，缺失任一立即抛
///   <see cref="InvalidOperationException"/>，不让缺口漏到运行时。
///
/// 【生命周期】Scoped（与 Router / Handler 同组）。构造发生在每次上行处理的作用域内，
///   校验成本可忽略。
/// </summary>
public sealed class AnShengEventDispatcher
{
    private readonly Dictionary<string, IAnShengEventHandler> _handlers;
    private readonly ILogger<AnShengEventDispatcher> _logger;

    /// <summary>
    /// 构造分发器并建索引。
    /// </summary>
    /// <param name="handlers">DI 注入的全部事件 Handler。</param>
    /// <param name="logger">日志器。</param>
    /// <exception cref="InvalidOperationException">
    /// 同一 method 被多个 Handler 注册，或 7 个方法未全覆盖。
    /// </exception>
    public AnShengEventDispatcher(
        IEnumerable<IAnShengEventHandler> handlers,
        ILogger<AnShengEventDispatcher> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _handlers = new Dictionary<string, IAnShengEventHandler>(StringComparer.Ordinal);
        foreach (var handler in handlers)
        {
            if (handler == null || string.IsNullOrWhiteSpace(handler.Method))
            {
                continue;
            }

            if (_handlers.ContainsKey(handler.Method))
            {
                throw new InvalidOperationException(
                    $"事件方法 {handler.Method} 被多个 Handler 注册，禁止重复覆盖。");
            }

            _handlers[handler.Method] = handler;
        }

        // 启动期自检：硬 ∪ 软 共 7 个方法必须全部覆盖。
        var missing = AnShengMessageRouter.AllEventMethods
            .Where(m => !_handlers.ContainsKey(m))
            .ToList();
        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                $"事件 Handler 覆盖不完整，缺失方法：{string.Join(", ", missing)}");
        }
    }

    /// <summary>
    /// 派发一条事件报文。
    /// </summary>
    /// <param name="ctx">上行上下文。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task DispatchAsync(AnShengUplinkContext ctx, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ctx);

        if (string.IsNullOrWhiteSpace(ctx.Method))
        {
            return;
        }

        if (!_handlers.TryGetValue(ctx.Method, out var handler))
        {
            _logger.LogWarning(
                "[AnShengDispatcher] 无 Handler 覆盖方法 {Method}，事件被忽略 imei={Imei}",
                ctx.Method, ctx.Imei);
            return;
        }

        await handler.HandleAsync(ctx, cancellationToken).ConfigureAwait(false);
    }
}
