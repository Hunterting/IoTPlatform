using IoTPlatform.Infrastructure.Protocol.AnSheng;
using System;
using System.Collections.Generic;
using Xunit;

namespace IoTPlatform.AnSheng.Tests;

/// <summary>
/// <see cref="AnShengUplinkHub"/> 的契约测试。
///
/// 这个类只有一件事要证明：<b>总线在任何输入下都不会把异常漏给 MQTT 接收线程</b>。
/// 一旦漏了，整条上行链路会被打断，表现为「全平台设备集体离线」——
/// 这是本次改造里破坏力最大的一种回归，所以值得单独一组用例守着。
/// </summary>
[Collection(AnShengStaticStateCollection.Name)]
public sealed class AnShengUplinkHubTests : IDisposable
{
    private const string Imei = "864900000000001";
    private const string Method = "getDevInfo";

    /// <summary>
    /// 每个用例开始前清空订阅，避免上一个用例的订阅者串扰。
    /// </summary>
    public AnShengUplinkHubTests() => AnShengUplinkHub.Reset();

    /// <summary>
    /// 用例结束后同样清空，保证不把订阅泄漏给别的测试类。
    /// </summary>
    public void Dispose() => AnShengUplinkHub.Reset();

    /// <summary>
    /// 正常发布应当把完整载荷送到订阅者手上。
    /// </summary>
    [Fact]
    public void Publish_Should_Deliver_Full_Payload_To_Subscriber()
    {
        AnShengUplinkEventArgs? received = null;
        object? observedSender = null;
        void Handler(object? sender, AnShengUplinkEventArgs e)
        {
            observedSender = sender;
            received = e;
        }

        AnShengUplinkHub.Uplink += Handler;
        try
        {
            var message = new AnShengMessage { Method = Method, Imei = Imei, Result = "ok" };
            var before = DateTime.UtcNow.AddSeconds(-1);

            AnShengUplinkHub.Publish(Imei, Method, message, "{\"method\":\"getDevInfo\"}");

            Assert.NotNull(received);
            Assert.Equal(Imei, received!.Imei);
            Assert.Equal(Method, received.Method);
            Assert.Same(message, received.Message);
            Assert.Equal("{\"method\":\"getDevInfo\"}", received.RawPayload);
            Assert.InRange(received.ReceivedAt, before, DateTime.UtcNow.AddSeconds(1));

            // 事件源恒为 null：总线是静态的，没有「发送者实例」这回事。
            Assert.Null(observedSender);
        }
        finally
        {
            AnShengUplinkHub.Uplink -= Handler;
        }
    }

    /// <summary>
    /// IMEI 或 method 缺失的报文无法关联到任何等待者，应当被直接丢弃。
    /// </summary>
    /// <param name="imei">待发布的 IMEI。</param>
    /// <param name="method">待发布的方法名。</param>
    [Theory]
    [InlineData(null, "getDevInfo")]
    [InlineData("", "getDevInfo")]
    [InlineData("   ", "getDevInfo")]
    [InlineData("864900000000001", null)]
    [InlineData("864900000000001", "")]
    [InlineData("864900000000001", "   ")]
    [InlineData(null, null)]
    public void Publish_Should_Ignore_Message_Without_Imei_Or_Method(string? imei, string? method)
    {
        var hits = 0;
        void Handler(object? sender, AnShengUplinkEventArgs e) => hits++;

        AnShengUplinkHub.Uplink += Handler;
        try
        {
            AnShengUplinkHub.Publish(imei, method, new AnShengMessage());
            Assert.Equal(0, hits);
        }
        finally
        {
            AnShengUplinkHub.Uplink -= Handler;
        }
    }

    /// <summary>
    /// 没有订阅者时发布必须是无害的空操作。
    /// </summary>
    [Fact]
    public void Publish_Should_Be_NoOp_When_No_Subscriber()
    {
        var exception = Record.Exception(() =>
            AnShengUplinkHub.Publish(Imei, Method, new AnShengMessage()));

        Assert.Null(exception);
    }

    /// <summary>
    /// 一个订阅者抛异常，不得影响其余订阅者收报，更不得把异常抛回发布方。
    /// </summary>
    [Fact]
    public void Publish_Should_Isolate_Throwing_Subscriber()
    {
        var order = new List<string>();
        void First(object? sender, AnShengUplinkEventArgs e)
        {
            order.Add("first");
            throw new InvalidOperationException("订阅者 A 故意炸掉");
        }

        void Second(object? sender, AnShengUplinkEventArgs e) => order.Add("second");

        void Third(object? sender, AnShengUplinkEventArgs e)
        {
            order.Add("third");
            throw new NotSupportedException("订阅者 C 也炸");
        }

        void Fourth(object? sender, AnShengUplinkEventArgs e) => order.Add("fourth");

        AnShengUplinkHub.Uplink += First;
        AnShengUplinkHub.Uplink += Second;
        AnShengUplinkHub.Uplink += Third;
        AnShengUplinkHub.Uplink += Fourth;
        try
        {
            var exception = Record.Exception(() =>
                AnShengUplinkHub.Publish(Imei, Method, new AnShengMessage()));

            Assert.Null(exception);
            Assert.Equal(new[] { "first", "second", "third", "fourth" }, order);
        }
        finally
        {
            AnShengUplinkHub.Uplink -= First;
            AnShengUplinkHub.Uplink -= Second;
            AnShengUplinkHub.Uplink -= Third;
            AnShengUplinkHub.Uplink -= Fourth;
        }
    }

    /// <summary>
    /// <c>message</c> 为 null（解析失败）时也要照常投递，让订阅者自行决定怎么处理。
    /// </summary>
    [Fact]
    public void Publish_Should_Deliver_Even_When_Message_Is_Null()
    {
        AnShengUplinkEventArgs? received = null;
        void Handler(object? sender, AnShengUplinkEventArgs e) => received = e;

        AnShengUplinkHub.Uplink += Handler;
        try
        {
            AnShengUplinkHub.Publish(Imei, Method, null, "not-a-json");

            Assert.NotNull(received);
            Assert.Null(received!.Message);
            Assert.Equal("not-a-json", received.RawPayload);
        }
        finally
        {
            AnShengUplinkHub.Uplink -= Handler;
        }
    }

    /// <summary>
    /// 同一个委托订阅两次会收到两次——这是 .NET 多播委托的既定语义，
    /// 锁死它是为了提醒：探测服务必须是 Singleton，重复注册会导致重复消费。
    /// </summary>
    [Fact]
    public void Publish_Should_Invoke_Duplicate_Subscription_Twice()
    {
        var hits = 0;
        void Handler(object? sender, AnShengUplinkEventArgs e) => hits++;

        AnShengUplinkHub.Uplink += Handler;
        AnShengUplinkHub.Uplink += Handler;
        try
        {
            AnShengUplinkHub.Publish(Imei, Method, new AnShengMessage());
            Assert.Equal(2, hits);
        }
        finally
        {
            AnShengUplinkHub.Uplink -= Handler;
            AnShengUplinkHub.Uplink -= Handler;
        }
    }

    /// <summary>
    /// <c>Reset</c> 应当清空全部订阅者。
    /// 这也是它<b>只能用于单元测试</b>的原因：集成测试里 Singleton 探测服务不会被重建。
    /// </summary>
    [Fact]
    public void Reset_Should_Remove_All_Subscribers()
    {
        var hits = 0;
        void Handler(object? sender, AnShengUplinkEventArgs e) => hits++;

        AnShengUplinkHub.Uplink += Handler;
        AnShengUplinkHub.Publish(Imei, Method, new AnShengMessage());
        Assert.Equal(1, hits);

        AnShengUplinkHub.Reset();
        AnShengUplinkHub.Publish(Imei, Method, new AnShengMessage());

        Assert.Equal(1, hits);
    }

    /// <summary>
    /// 连续 Reset 必须幂等。
    /// </summary>
    [Fact]
    public void Reset_Should_Be_Idempotent()
    {
        var exception = Record.Exception(() =>
        {
            AnShengUplinkHub.Reset();
            AnShengUplinkHub.Reset();
            AnShengUplinkHub.Publish(Imei, Method, new AnShengMessage());
        });

        Assert.Null(exception);
    }
}
