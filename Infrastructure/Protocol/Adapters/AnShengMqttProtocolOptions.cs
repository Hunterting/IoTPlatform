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

    /// <summary>数据发布主题模式（通配符），设备 publish 到 /devtoser/pub/{imei}</summary>
    public string PublishTopicPattern { get; set; } = "/devtoser/pub/+";

    /// <summary>Will 遗愿主题模式，设备掉线时 Broker 发布到 /devtoser/will/{imei}</summary>
    public string WillTopicPattern { get; set; } = "/devtoser/will/+";

    /// <summary>命令下发主题模板，平台 publish 到 /sertodev/{imei}</summary>
    public string SubscribeTopicTemplate { get; set; } = "/sertodev/{imei}";

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
