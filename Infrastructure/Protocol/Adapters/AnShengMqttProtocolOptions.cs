namespace IoTPlatform.Infrastructure.Protocol.Adapters;

/// <summary>
/// 安圣 MQTT 协议配置选项
/// </summary>
public class AnShengMqttProtocolOptions
{
    /// <summary>MQTT Broker 地址</summary>
    public string Host { get; set; } = "localhost";

    /// <summary>MQTT Broker 端口</summary>
    public int Port { get; set; } = 1883;

    /// <summary>MQTT 用户名</summary>
    public string? Username { get; set; }

    /// <summary>MQTT 密码</summary>
    public string? Password { get; set; }

    /// <summary>客户端 ID 前缀</summary>
    public string ClientIdPrefix { get; set; } = "iot_platform_ansheng";

    /// <summary>是否清理会话</summary>
    public bool CleanSession { get; set; } = true;

    /// <summary>连接超时（秒）</summary>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>Keep-Alive 间隔（秒）</summary>
    public int KeepAliveSeconds { get; set; } = 60;

    /// <summary>MQTT QoS 级别（0/1/2），安圣 WiFi 设备仅支持 0/1</summary>
    public int QosLevel { get; set; } = 1;

    /// <summary>
    /// 设备上行主题模式（通配符）。
    /// 安圣二开设备统一 publish 到 <c>/iot/server/iot-board/{imei}</c>，
    /// 业务数据与 will 遗愿<b>共用同一主题</b>，靠报文内 <c>method</c> 区分。
    /// </summary>
    public string PublishTopicPattern { get; set; } = "/iot/server/iot-board/+";

    /// <summary>
    /// Will 遗愿主题模式。安圣二开协议与 <see cref="PublishTopicPattern"/> 相同，
    /// 掉线判定<b>不依赖主题前缀</b>，而是判断 <c>method == "close"</c>。
    /// 两者相同时适配器只订阅一次，避免重复投递。
    /// </summary>
    public string WillTopicPattern { get; set; } = "/iot/server/iot-board/+";

    /// <summary>命令下发主题模板，平台 publish 到 <c>/iot/client/iot-board/{imei}</c></summary>
    public string SubscribeTopicTemplate { get; set; } = "/iot/client/iot-board/{imei}";

    /// <summary>同一 IMEI 两次命令下发的最小间隔（毫秒），协议要求 ≥100ms 防止命令粘连</summary>
    public int CommandMinIntervalMs { get; set; } = 100;

    /// <summary>是否在设备上线时自动配置自动上报</summary>
    public bool AutoConfigureAutoReport { get; set; } = true;

    /// <summary>默认自动上报设置</summary>
    public AnShengAutoReportSettings DefaultAutoReport { get; set; } = new();
}

/// <summary>
/// 安圣设备自动上报间隔设置
/// </summary>
public class AnShengAutoReportSettings
{
    /// <summary>设备状态上报间隔（秒），默认 60</summary>
    public int? GetDevStatusSec { get; set; } = 60;

    /// <summary>设备状态查询参数</summary>
    public string? GetDevStatusQ { get; set; } = "";

    /// <summary>订单进度上报间隔（秒），默认 300</summary>
    public int? OrderUpSec { get; set; } = 300;

    /// <summary>RS485 轮询间隔（秒），0 = 关闭</summary>
    public int? Rs485Sec { get; set; } = 0;
}
