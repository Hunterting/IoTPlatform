using Microsoft.AspNetCore.SignalR;

namespace IoTPlatform.Hubs;

/// <summary>
/// 设备数据推送Hub
/// </summary>
public class DeviceHub : Hub
{
    /// <summary>
    /// 客户端连接时
    /// </summary>
    public override async Task OnConnectedAsync()
    {
        var appCode = Context.GetHttpContext()?.User.FindFirst("AppCode")?.Value;
        if (!string.IsNullOrEmpty(appCode))
        {
            await Groups.AddToGroupAsync(appCode);
        }
        await base.OnConnectedAsync();
    }

    /// <summary>
    /// 客户端断开时
    /// </summary>
    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var appCode = Context.GetHttpContext()?.User.FindFirst("AppCode")?.Value;
        if (!string.IsNullOrEmpty(appCode))
        {
            await Groups.RemoveFromGroupAsync(appCode);
        }
        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// 发送设备数据
    /// </summary>
    public async Task SendDeviceData(object data)
    {
        var appCode = Context.GetHttpContext()?.User.FindFirst("AppCode")?.Value;
        if (!string.IsNullOrEmpty(appCode))
        {
            await Clients.Group(appCode).SendAsync("DeviceData", data);
        }
    }

    /// <summary>
    /// 发送告警通知
    /// </summary>
    public async Task SendAlert(object alert)
    {
        var appCode = Context.GetHttpContext()?.User.FindFirst("AppCode")?.Value;
        if (!string.IsNullOrEmpty(appCode))
        {
            await Clients.Group(appCode).SendAsync("Alert", alert);
        }
    }
}
