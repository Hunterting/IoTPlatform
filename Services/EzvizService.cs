using IoTPlatform.Configuration;
using IoTPlatform.Data;
using IoTPlatform.DTOs.Responses;
using IoTPlatform.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Globalization;
using System.Text.Json;

namespace IoTPlatform.Services;

/// <summary>萤石接口调用异常（携带萤石返回码，便于 Controller 映射为友好提示）</summary>
public class EzvizException : Exception
{
    public string? EzvizCode { get; }
    public EzvizException(string message, string? ezvizCode = null) : base(message) { EzvizCode = ezvizCode; }
}

/// <summary>
/// 萤石开放平台后端代理实现。
///
/// 依据 docs/萤石开放平台_API_云接入_OPEN_API.docx：
///  - 所有接口 POST + application/x-www-form-urlencoded，响应信封 { code:"200", msg, data }
///  - token/get 用 appKey + appSecret 换 accessToken（expireTime 毫秒时间戳）
///  - 其余接口统一带 accessToken
/// </summary>
public class EzvizService : IEzvizService
{
    private readonly IOptions<EzvizOptions> _options;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<EzvizService> _logger;
    private readonly AppDbContext _dbContext;

    // 进程内 token 缓存（appSecret 变更或过期前自动刷新）
    private static readonly SemaphoreSlim TokenLock = new(1, 1);
    private static string? _cachedToken;
    private static long _cachedExpireTimeMs;
    private static string? _cachedCredential; // appKey+appSecret 指纹，凭证变更即失效

    public EzvizService(
        IOptions<EzvizOptions> options,
        IHttpClientFactory httpClientFactory,
        ILogger<EzvizService> logger,
        AppDbContext dbContext)
    {
        _options = options;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _dbContext = dbContext;
    }

    private EzvizOptions Opt => _options.Value;

    public EzvizConfigStatusDto GetConfigStatus()
    {
        var configured = Opt.IsConfigured;
        return new EzvizConfigStatusDto
        {
            Configured = configured,
            BaseUrl = Opt.BaseUrl,
            LiveProtocol = Opt.LiveProtocol,
            Message = configured
                ? "萤石凭证已配置"
                : "未配置萤石 appKey / appSecret：请在 appsettings.json 的 Ezviz 节或环境变量 Ezviz__AppKey / Ezviz__AppSecret 中填写后重启服务。当前不显示任何模拟画面。"
        };
    }

    // ── Token ──────────────────────────────────────────────────────────────────

    private void EnsureConfigured()
    {
        if (!Opt.IsConfigured)
            throw new EzvizException(GetConfigStatus().Message, "NOT_CONFIGURED");
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken ct)
    {
        EnsureConfigured();

        var credential = Opt.AppKey + ":" + Opt.AppSecret;
        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        // expireTime 前 5 分钟刷新；凭证变化立即作废
        if (_cachedToken != null && _cachedCredential == credential && _cachedExpireTimeMs - nowMs > 5 * 60 * 1000)
            return _cachedToken;

        await TokenLock.WaitAsync(ct);
        try
        {
            nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (_cachedToken != null && _cachedCredential == credential && _cachedExpireTimeMs - nowMs > 5 * 60 * 1000)
                return _cachedToken;

            var form = new Dictionary<string, string>
            {
                ["appKey"] = Opt.AppKey,
                ["appSecret"] = Opt.AppSecret,
            };
            using var doc = await PostFormRawAsync("/api/lapp/token/get", form, ct);
            var root = doc.RootElement;
            EnsureSuccess(root);
            var data = GetData(root);
            var token = GetString(data, "accessToken");
            if (string.IsNullOrEmpty(token))
                throw new EzvizException("萤石 token/get 未返回 accessToken", GetCode(root));

            long expire = GetLong(data, "expireTime") ?? (nowMs + 7 * 24 * 60 * 60 * 1000);
            _cachedToken = token;
            _cachedExpireTimeMs = expire;
            _cachedCredential = credential;
            _logger.LogInformation("萤石 accessToken 已刷新，过期时间 {ExpireUtc}",
                DateTimeOffset.FromUnixTimeMilliseconds(expire).UtcDateTime);
            return token;
        }
        finally
        {
            TokenLock.Release();
        }
    }

    // ── 设备列表 ──────────────────────────────────────────────────────────────

    public async Task<EzvizPagedResult<EzvizDeviceDto>> GetDevicesAsync(
        int pageStart, int pageSize, string? appCode, CancellationToken ct = default)
    {
        var token = await GetAccessTokenAsync(ct);
        var page = Math.Max(0, pageStart);
        var size = Math.Clamp(pageSize, 1, 50);
        var result = new EzvizPagedResult<EzvizDeviceDto> { Page = page, PageSize = size };

        // ① device/list：自有设备（主账号添加的设备）
        var ownedCount = 0;
        using (var doc = await PostFormRawAsync("/api/lapp/device/list", new Dictionary<string, string>
        {
            ["accessToken"] = token,
            ["pageStart"] = page.ToString(CultureInfo.InvariantCulture),
            ["pageSize"] = size.ToString(CultureInfo.InvariantCulture),
        }, ct))
        {
            var root = doc.RootElement;
            EnsureSuccess(root);
            var data = GetData(root);
            if (data.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in data.EnumerateArray())
                {
                    result.Items.Add(new EzvizDeviceDto
                    {
                        DeviceSerial = GetString(el, "deviceSerial"),
                        DeviceName = GetString(el, "deviceName"),
                        DeviceType = GetString(el, "deviceType"),
                        Status = GetInt(el, "status") ?? 0,
                        Defence = GetInt(el, "defence") ?? 0,
                        DeviceVersion = GetString(el, "deviceVersion"),
                        ParentCategory = GetString(el, "parentCategory"),
                        NetAddress = GetString(el, "netAddress"),
                        AddTime = GetLong(el, "addTime"),
                        UpdateTime = GetLong(el, "updateTime"),
                        Source = "owned",
                    });
                }
            }
            if (root.TryGetProperty("page", out var pageEl) && pageEl.ValueKind == JsonValueKind.Object)
                ownedCount = GetInt(pageEl, "total") ?? result.Items.Count;
        }

        // ② camera/list（不传 deviceSerial）：全账号可见监控点，含【分享设备】。
        //    文档「获取视频通道列表」明确此接口列的是监控点；实测个人版账号自有设备为 0、
        //    但分享设备 D72071566 只在这里出现。两路合并按 serial+channel 去重，避免遗漏或重复。
        try
        {
            using var doc2 = await PostFormRawAsync("/api/lapp/camera/list", new Dictionary<string, string>
            {
                ["accessToken"] = token,
                ["pageStart"] = page.ToString(CultureInfo.InvariantCulture),
                ["pageSize"] = size.ToString(CultureInfo.InvariantCulture),
            }, ct);
            var root2 = doc2.RootElement;
            EnsureSuccess(root2);
            var data2 = GetData(root2);
            if (data2.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in data2.EnumerateArray())
                {
                    var serial = GetString(el, "deviceSerial");
                    if (string.IsNullOrEmpty(serial)) continue;
                    var channelNo = GetInt(el, "channelNo") ?? 1;
                    var key = serial + ":" + channelNo;
                    if (result.Items.Any(x => (x.DeviceSerial + ":" + x.ChannelNo) == key)) continue;

                    var isShared = GetString(el, "isShared");
                    result.Items.Add(new EzvizDeviceDto
                    {
                        DeviceSerial = serial,
                        DeviceName = GetString(el, "channelName") ?? serial,
                        DeviceType = GetString(el, "parentCategory"),
                        Status = GetInt(el, "status") ?? 0,
                        PicUrl = GetString(el, "picUrl"),
                        ChannelNo = channelNo,
                        // isShared: "1"=主账号自有通道, "2"=被分享通道
                        Source = isShared == "2" ? "shared" : "owned-channel",
                        IsShared = isShared == "2",
                        // 分享设备：真机实测 live/address、capture、status/get 均被拒（20018/10031），
                        // 标记为不可操作，前端据此提示「分享设备权限不足」而非反复请求失败。
                        Operable = isShared != "2",
                    });
                }
            }
        }
        catch (EzvizException ex)
        {
            // camera/list 失败不阻断 device/list 结果（例如权限受限时仍展示自有设备）
            _logger.LogWarning("萤石 camera/list 回退失败（不影响 device/list 结果）: {Message}", ex.Message);
        }

        result.TotalCount = Math.Max(ownedCount, result.Items.Count);
        await ApplyLocalBindingsAsync(result.Items, appCode, ct);
        return result;
    }

    /// <summary>把萤石设备与本地 cameras 表的区域绑定合并（萤石无区域概念）</summary>
    private async Task ApplyLocalBindingsAsync(List<EzvizDeviceDto> devices, string? appCode, CancellationToken ct)
    {
        if (devices.Count == 0) return;
        // StreamUrl 保存 "ezviz:{deviceSerial}:{channelNo}"（见 BindCameraAsync）。
        // 绑定量很小，直接取全部 ezviz: 前缀行在内存做前缀匹配，避免 EF 翻译 Contains(prefix) 的歧义。
        var bindings = await _dbContext.Cameras
            .AsNoTracking()
            .Include(c => c.Area)
            .Where(c => c.StreamUrl != null && c.StreamUrl.StartsWith("ezviz:"))
            .Select(c => new { c.StreamUrl, c.AreaId, AreaName = c.Area != null ? c.Area.Name : null })
            .ToListAsync(ct);
        foreach (var d in devices)
        {
            if (string.IsNullOrEmpty(d.DeviceSerial)) continue;
            var prefix = "ezviz:" + d.DeviceSerial + ":";
            var b = bindings.FirstOrDefault(x => x.StreamUrl != null &&
                x.StreamUrl.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
            if (b == null) continue;
            d.AreaId = b.AreaId;
            d.AreaName = b.AreaName;
        }
    }

    // ── 通道列表 ──────────────────────────────────────────────────────────────

    public async Task<List<EzvizCameraDto>> GetCamerasAsync(string deviceSerial, CancellationToken ct = default)
    {
        var token = await GetAccessTokenAsync(ct);
        var form = new Dictionary<string, string>
        {
            ["accessToken"] = token,
            ["deviceSerial"] = deviceSerial,
        };
        using var doc = await PostFormRawAsync("/api/lapp/camera/list", form, ct);
        var root = doc.RootElement;
        EnsureSuccess(root);
        var list = new List<EzvizCameraDto>();
        var data = GetData(root);
        if (data.ValueKind == JsonValueKind.Array)
        {
            foreach (var el in data.EnumerateArray())
            {
                list.Add(new EzvizCameraDto
                {
                    DeviceSerial = GetString(el, "deviceSerial") ?? deviceSerial,
                    ChannelNo = GetInt(el, "channelNo") ?? 1,
                    ChannelName = GetString(el, "channelName"),
                    Status = GetInt(el, "status") ?? 0,
                    PicUrl = GetString(el, "picUrl"),
                    IsEncrypt = GetInt(el, "isEncrypt") == 1,
                });
            }
        }
        return list;
    }

    // ── 直播/回放地址 ────────────────────────────────────────────────────────

    public async Task<EzvizLiveAddressDto> GetLiveAddressAsync(EzvizLiveAddressRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.DeviceSerial))
            throw new EzvizException("deviceSerial 不能为空", "10001");
        var token = await GetAccessTokenAsync(ct);
        var form = new Dictionary<string, string>
        {
            ["accessToken"] = token,
            ["deviceSerial"] = request.DeviceSerial,
            ["channelNo"] = Math.Max(1, request.ChannelNo).ToString(CultureInfo.InvariantCulture),
            ["protocol"] = Opt.LiveProtocol.ToString(CultureInfo.InvariantCulture),
            ["expireTime"] = Opt.LiveExpireSeconds.ToString(CultureInfo.InvariantCulture),
            ["type"] = request.Type.ToString(CultureInfo.InvariantCulture),
        };
        if (!string.IsNullOrWhiteSpace(request.StartTime)) form["startTime"] = request.StartTime!;
        if (!string.IsNullOrWhiteSpace(request.StopTime)) form["stopTime"] = request.StopTime!;

        using var doc = await PostFormRawAsync("/api/lapp/v2/live/address/get", form, ct);
        var root = doc.RootElement;
        EnsureSuccess(root);
        var data = GetData(root);
        return new EzvizLiveAddressDto
        {
            Id = GetString(data, "id"),
            Url = GetString(data, "url"),
            ExpireTime = GetLong(data, "expireTime") ?? 0,
            DeviceSerial = request.DeviceSerial,
            ChannelNo = request.ChannelNo,
        };
    }

    // ── 抓拍 ──────────────────────────────────────────────────────────────────

    public async Task<EzvizCaptureDto> CaptureAsync(EzvizCaptureRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.DeviceSerial))
            throw new EzvizException("deviceSerial 不能为空", "10001");
        var token = await GetAccessTokenAsync(ct);
        var form = new Dictionary<string, string>
        {
            ["accessToken"] = token,
            ["deviceSerial"] = request.DeviceSerial,
            ["channelNo"] = Math.Max(1, request.ChannelNo).ToString(CultureInfo.InvariantCulture),
        };
        using var doc = await PostFormRawAsync("/api/lapp/device/capture", form, ct);
        var root = doc.RootElement;
        EnsureSuccess(root);
        var data = GetData(root);
        return new EzvizCaptureDto
        {
            PicUrl = GetString(data, "picUrl"),
            DeviceSerial = request.DeviceSerial,
            ChannelNo = request.ChannelNo,
        };
    }

    // ── 回放录像查询 ──────────────────────────────────────────────────────────

    public async Task<List<EzvizPlaybackRecordDto>> GetPlaybackAsync(EzvizPlaybackRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.DeviceSerial))
            throw new EzvizException("deviceSerial 不能为空", "10001");
        var token = await GetAccessTokenAsync(ct);
        var form = new Dictionary<string, string>
        {
            ["accessToken"] = token,
            ["deviceSerial"] = request.DeviceSerial,
            ["channelNo"] = Math.Max(1, request.ChannelNo).ToString(CultureInfo.InvariantCulture),
            ["startTime"] = request.StartTime,
            ["endTime"] = request.EndTime,
            ["recType"] = request.RecType.ToString(CultureInfo.InvariantCulture),
        };
        using var doc = await PostFormRawAsync("/api/lapp/video/by/time", form, ct);
        var root = doc.RootElement;
        EnsureSuccess(root);
        var list = new List<EzvizPlaybackRecordDto>();
        var data = GetData(root);
        if (data.ValueKind == JsonValueKind.Array)
        {
            foreach (var el in data.EnumerateArray())
            {
                list.Add(new EzvizPlaybackRecordDto
                {
                    DeviceSerial = GetString(el, "deviceSerial") ?? request.DeviceSerial,
                    ChannelNo = GetInt(el, "channelNo") ?? request.ChannelNo,
                    StartTime = GetLong(el, "startTime"),
                    EndTime = GetLong(el, "endTime"),
                    RecType = GetInt(el, "recType") ?? 0,
                    FileId = GetString(el, "fileId"),
                    FileName = GetString(el, "fileName"),
                    CoverPic = GetString(el, "coverPic"),
                    DownloadPath = GetString(el, "downloadPath"),
                    FileSize = GetLong(el, "fileSize"),
                    VideoLong = GetLong(el, "videoLong"),
                });
            }
        }
        return list;
    }

    // ── 云台 ──────────────────────────────────────────────────────────────────

    public async Task PtzStartAsync(EzvizPtzRequest request, CancellationToken ct = default)
        => await PtzAsync("/api/lapp/device/ptz/start", request, ct);

    public async Task PtzStopAsync(EzvizPtzRequest request, CancellationToken ct = default)
        => await PtzAsync("/api/lapp/device/ptz/stop", request, ct);

    private async Task PtzAsync(string path, EzvizPtzRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.DeviceSerial))
            throw new EzvizException("deviceSerial 不能为空", "10001");
        var token = await GetAccessTokenAsync(ct);
        var form = new Dictionary<string, string>
        {
            ["accessToken"] = token,
            ["deviceSerial"] = request.DeviceSerial,
            ["channelNo"] = Math.Max(1, request.ChannelNo).ToString(CultureInfo.InvariantCulture),
            ["direction"] = request.Direction.ToString(CultureInfo.InvariantCulture),
            ["speed"] = request.Speed.ToString(CultureInfo.InvariantCulture),
        };
        using var doc = await PostFormRawAsync(path, form, ct);
        EnsureSuccess(doc.RootElement);
    }

    // ── 设备状态 ──────────────────────────────────────────────────────────────

    public async Task<EzvizDeviceStatusDto> GetDeviceStatusAsync(string deviceSerial, CancellationToken ct = default)
    {
        var token = await GetAccessTokenAsync(ct);
        var form = new Dictionary<string, string>
        {
            ["accessToken"] = token,
            ["deviceSerial"] = deviceSerial,
        };
        using var doc = await PostFormRawAsync("/api/lapp/device/status/get", form, ct);
        var root = doc.RootElement;
        EnsureSuccess(root);
        var data = GetData(root);
        var status = GetInt(data, "status") ?? 0;
        return new EzvizDeviceStatusDto
        {
            DeviceSerial = deviceSerial,
            Status = status,
            StatusText = status == 1 ? "在线" : "离线",
            Defence = GetInt(data, "defence") ?? 0,
        };
    }

    // ── 本地绑定 ──────────────────────────────────────────────────────────────

    public async Task<EzvizDeviceDto> BindCameraAsync(EzvizBindCameraRequest request, string? appCode, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.DeviceSerial))
            throw new EzvizException("deviceSerial 不能为空", "10001");
        var streamKey = $"ezviz:{request.DeviceSerial}:{request.ChannelNo}";
        var existing = await _dbContext.Cameras
            .FirstOrDefaultAsync(c => c.StreamUrl == streamKey, ct);
        if (existing == null)
        {
            existing = new Camera
            {
                Name = string.IsNullOrWhiteSpace(request.Name) ? request.DeviceSerial : request.Name,
                StreamUrl = streamKey,
                Protocol = "hls",
                Status = "online",
                AppCode = appCode,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            };
            _dbContext.Cameras.Add(existing);
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(request.Name)) existing.Name = request.Name;
            existing.UpdatedAt = DateTime.UtcNow;
        }
        existing.AreaId = request.AreaId;
        await _dbContext.SaveChangesAsync(ct);
        return new EzvizDeviceDto
        {
            DeviceSerial = request.DeviceSerial,
            DeviceName = existing.Name,
            AreaId = request.AreaId,
        };
    }

    // ── HTTP 底层 ────────────────────────────────────────────────────────────

    private async Task<JsonDocument> PostFormRawAsync(string path, Dictionary<string, string> form, CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient("Ezviz");
        client.BaseAddress = new Uri(Opt.BaseUrl);
        client.Timeout = TimeSpan.FromSeconds(Math.Max(1, Opt.TimeoutSeconds));
        using var content = new FormUrlEncodedContent(form);
        using var resp = await client.PostAsync(path, content, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
        {
            _logger.LogWarning("萤石 HTTP {Status} {Path}: {Body}", (int)resp.StatusCode, path, Truncate(body));
            throw new EzvizException($"萤石网关返回 HTTP {(int)resp.StatusCode}", "HTTP_" + (int)resp.StatusCode);
        }
        try
        {
            return JsonDocument.Parse(body);
        }
        catch (JsonException)
        {
            _logger.LogWarning("萤石响应非 JSON {Path}: {Body}", path, Truncate(body));
            throw new EzvizException("萤石网关响应格式异常", "INVALID_JSON");
        }
    }

    /// <summary>
    /// 常见萤石错误码的中文解释（文档「错误码」章节 + 真机实测）。
    /// 目的：账号权限/套餐类问题在界面上一眼可懂，而不是抛原始英文或一串数字。
    /// </summary>
    private static readonly Dictionary<string, string> ErrorHints = new(StringComparer.Ordinal)
    {
        ["20018"] = "当前账号不拥有该设备（多为被分享设备，直播/抓拍/状态接口被拒）",
        ["10031"] = "子账号或萤石用户没有该操作权限",
        ["10026"] = "设备数量超出个人版限制，需在萤石开放平台升级企业版",
        ["20007"] = "设备不在线",
        ["20002"] = "账号下无此设备",
        ["20014"] = "deviceSerial 不合法",
        ["10002"] = "accessToken 已过期（将自动刷新后重试）",
        ["10005"] = "appKey 异常或被冻结",
        ["10017"] = "appKey 不存在",
        ["10030"] = "appKey 与 appSecret 不匹配",
        ["60000"] = "设备不支持云台控制",
        ["60001"] = "用户无云台控制权限",
    };

    private static void EnsureSuccess(JsonElement root)
    {
        var code = GetCode(root);
        if (code == "200") return;
        var msg = GetString(root, "msg") ?? "萤石接口调用失败";
        var hint = code != null && ErrorHints.TryGetValue(code, out var h) ? "（" + h + "）" : string.Empty;
        throw new EzvizException($"萤石接口错误 [{code ?? "?"}]：{msg}{hint}", code);
    }

    private static string? GetCode(JsonElement root) => GetString(root, "code");

    private static JsonElement GetData(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("data", out var d)) return d;
        return default;
    }

    private static string? GetString(JsonElement el, string name)
    {
        if (el.ValueKind != JsonValueKind.Object || !el.TryGetProperty(name, out var v)) return null;
        // 兼容大小写（萤石文档字段为标准 camelCase，做个兜底）
        return v.ValueKind switch
        {
            JsonValueKind.String => v.GetString(),
            JsonValueKind.Number => v.ToString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null
        };
    }

    private static int? GetInt(JsonElement el, string name)
    {
        if (el.ValueKind != JsonValueKind.Object || !el.TryGetProperty(name, out var v)) return null;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i)) return i;
        if (v.ValueKind == JsonValueKind.String && int.TryParse(v.GetString(), out var s)) return s;
        return null;
    }

    private static long? GetLong(JsonElement el, string name)
    {
        if (el.ValueKind != JsonValueKind.Object || !el.TryGetProperty(name, out var v)) return null;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var l)) return l;
        if (v.ValueKind == JsonValueKind.String && long.TryParse(v.GetString(), out var s)) return s;
        return null;
    }

    private static string Truncate(string s) => s.Length > 500 ? s[..500] + "…" : s;
}
