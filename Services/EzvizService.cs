using IoTPlatform.Configuration;
using IoTPlatform.Data;
using IoTPlatform.DTOs.Responses;
using IoTPlatform.Infrastructure.Tenant;
using IoTPlatform.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Globalization;
using System.Net.Http;
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
    private readonly ITenantContextAccessor _tenantContextAccessor;

    // 进程内 token 缓存（appSecret 变更或过期前自动刷新）
    private static readonly SemaphoreSlim TokenLock = new(1, 1);
    private static string? _cachedToken;
    private static long _cachedExpireTimeMs;
    private static string? _cachedCredential; // appKey+appSecret 指纹，凭证变更即失效

    public EzvizService(
        IOptions<EzvizOptions> options,
        IHttpClientFactory httpClientFactory,
        ILogger<EzvizService> logger,
        AppDbContext dbContext,
        ITenantContextAccessor tenantContextAccessor)
    {
        _options = options;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _dbContext = dbContext;
        _tenantContextAccessor = tenantContextAccessor;
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
            SupportH265 = Opt.SupportH265,
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
        int pageStart, int pageSize, string? appCode, bool includeUnbound = false, CancellationToken ct = default)
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

        result.Items = await ApplyLocalBindingsAsync(result.Items, appCode, IsSuperAdmin(), includeUnbound, ct);
        result.TotalCount = result.Items.Count;
        return result;
    }

    /// <summary>
    /// 把萤石设备与本地 cameras 表的区域绑定合并，并按租户隔离设备列表。
    /// - 已绑定（当前租户 cameras 中存在 StreamUrl 前缀匹配）：合并 AreaId/AreaName 后保留。
    /// - 未绑定：默认仅超管可见（监控宫格只显示本租户已绑定摄像头）；
    ///   includeUnbound=true 时（绑定/添加摄像头选择器）普通租户也可选，否则无法自助绑定新设备。
    /// 注：全局查询过滤器已按当前租户 AppCode 隔离 cameras 行，故 bindings 本就是当前租户绑定。
    /// </summary>
    private async Task<List<EzvizDeviceDto>> ApplyLocalBindingsAsync(List<EzvizDeviceDto> devices, string? appCode, bool isSuperAdmin, bool includeUnbound, CancellationToken ct)
    {
        var kept = new List<EzvizDeviceDto>();
        if (devices.Count == 0) return kept;
        // StreamUrl 保存 "ezviz:{deviceSerial}:{channelNo}"（见 BindCameraAsync）。
        var bindings = await _dbContext.Cameras
            .AsNoTracking()
            .Include(c => c.Area)
            .Where(c => c.StreamUrl != null && c.StreamUrl.StartsWith("ezviz:"))
            .Select(c => new { c.StreamUrl, c.AreaId, AreaName = c.Area != null ? c.Area.Name : null })
            .ToListAsync(ct);

        // 当前租户已绑定的 deviceSerial 集合（全局过滤器已按 AppCode 隔离）
        var boundSerials = new HashSet<string>(
            bindings.Select(b => ExtractSerial(b.StreamUrl!)).OfType<string>(),
            StringComparer.OrdinalIgnoreCase);

        foreach (var d in devices)
        {
            if (string.IsNullOrEmpty(d.DeviceSerial)) continue;
            // 未绑定：超管或绑定选择器(includeUnbound)可见，普通租户监控宫格丢弃
            if (!boundSerials.Contains(d.DeviceSerial))
            {
                if (isSuperAdmin || includeUnbound) kept.Add(d);
                continue;
            }
            // 已绑定：合并区域归属后保留
            var prefix = "ezviz:" + d.DeviceSerial + ":";
            var b = bindings.FirstOrDefault(x => x.StreamUrl != null &&
                x.StreamUrl.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
            if (b != null)
            {
                d.AreaId = b.AreaId;
                d.AreaName = b.AreaName;
            }
            kept.Add(d);
        }
        return kept;
    }

    /// <summary>
    /// 按「当前租户 + deviceSerial」解析出 cameras 行。
    /// - 普通租户：全局查询过滤器已按 AppCode 隔离，只匹配本租户绑定；
    /// - 超管：过滤器被跳过，可匹配任意租户绑定（便于运维）。
    /// 返回 null 表示当前租户未绑定该设备（= 无权访问）。
    /// </summary>
    private async Task<Camera?> ResolveTenantCameraAsync(string deviceSerial, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(deviceSerial)) return null;
        // 注意：Pomelo MySQL 默认无法把 StartsWith(string, StringComparison) 翻译成 SQL（会抛
        // "Translation of method 'string.StartsWith' failed"）。改用 EF.Functions.Like，既可翻译，
        // 又保持 deviceSerial 大小写不敏感（MySQL 默认 collation 大小写不敏感）。
        var pattern = "ezviz:" + deviceSerial + ":%";
        return await _dbContext.Cameras
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.StreamUrl != null && EF.Functions.Like(c.StreamUrl, pattern), ct);
    }

    /// <summary>当前请求是否为超管（与 AppDbContext 全局过滤器判定一致）</summary>
    private bool IsSuperAdmin() => _tenantContextAccessor?.Current?.IsSuperAdmin ?? false;

    /// <summary>从 "ezviz:{serial}:{channel}" 解析 deviceSerial</summary>
    private static string? ExtractSerial(string streamUrl)
    {
        const string prefix = "ezviz:";
        if (!streamUrl.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;
        var rest = streamUrl.Substring(prefix.Length);
        var idx = rest.LastIndexOf(':');
        return idx > 0 ? rest.Substring(0, idx) : rest;
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
        // 所有权校验：解析本租户绑定的 cameras 行（超管可操作任意设备）
        var cam = await ResolveTenantCameraAsync(request.DeviceSerial, ct);
        if (cam == null && !IsSuperAdmin())
            throw new EzvizException("无权访问该摄像头（不属于当前租户）", "40301");

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
        // ── H265 支持开关（全局配置 + 请求级覆盖）─────────────────────────────
        var supportH265 = (request.SupportH265 ?? Opt.SupportH265) != 0 ? 1 : 0;
        if (supportH265 == 1)
        {
            form["supportH265"] = "1";
            // HLS + supportH265=1 时萤石只接受 fMP4(1)；TS 无法承载 H265。硬联动，不另开配置项。
            if (Opt.LiveProtocol == 2) form["containerFormat"] = "1";
        }
        if (request.Quality is int q && (q == 1 || q == 2))
            form["quality"] = q.ToString(CultureInfo.InvariantCulture);

        _logger.LogInformation(
            "EZVIZ 取流 device={Device} ch={Ch} protocol={Proto} supportH265={H265} containerFormat={Cf} quality={Q}",
            request.DeviceSerial, Math.Max(1, request.ChannelNo), Opt.LiveProtocol, supportH265,
            form.TryGetValue("containerFormat", out var cf) ? cf : "0",
            request.Quality?.ToString() ?? "default");

        // 设备加密：优先用请求里的验证码，否则用绑定存储的 EncryptionCode（修复 60019）
        // 注意：萤石 v2/live/address/get 接口的视频加密密码参数名是 code（见
        // docs/萤石开放平台_API_云接入_OPEN_API.docx「9. 获取播放地址」参数表 L10589），不是 ewcode。
        // 之前误用 ewcode 导致验证码被萤石忽略、加密设备始终 60019。
        var ewCode = !string.IsNullOrWhiteSpace(request.EwCode) ? request.EwCode
            : (cam != null ? cam.EncryptionCode : null);
        if (!string.IsNullOrWhiteSpace(ewCode)) form["code"] = ewCode!;
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
        if (await ResolveTenantCameraAsync(request.DeviceSerial, ct) == null && !IsSuperAdmin())
            throw new EzvizException("无权访问该摄像头（不属于当前租户）", "40301");
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
        if (await ResolveTenantCameraAsync(request.DeviceSerial, ct) == null && !IsSuperAdmin())
            throw new EzvizException("无权访问该摄像头（不属于当前租户）", "40301");
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
        if (await ResolveTenantCameraAsync(request.DeviceSerial, ct) == null && !IsSuperAdmin())
            throw new EzvizException("无权访问该摄像头（不属于当前租户）", "40301");
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

    // ── 关闭设备视频加密 ──────────────────────────────────────────────────────

    /// <summary>
    /// 关闭设备视频加密（device/encrypt/off）。
    /// 【设备侧配置变更，需调用方显式确认】执行后会改变摄像头安全配置，仅用于加密设备改用 HLS 取流
    /// （萤石 HLS 不支持加密传输，否则取流必报 60019）。无验证码参数，使用主账号 token。
    /// 萤石返回 60016「加密未开启，无需关闭」时按幂等成功处理；其余失败由 EnsureSuccess 抛 EzvizException。
    /// </summary>
    public async Task CloseEncryptionAsync(string deviceSerial, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(deviceSerial))
            throw new EzvizException("deviceSerial 不能为空", "10001");
        // 所有权校验：复用与 GetLiveAddressAsync / PtzAsync 一致的租户策略（超管可操作任意设备）
        if (await ResolveTenantCameraAsync(deviceSerial, ct) == null && !IsSuperAdmin())
            throw new EzvizException("无权访问该摄像头（不属于当前租户）", "40301");

        var token = await GetAccessTokenAsync(ct);
        var form = new Dictionary<string, string>
        {
            ["accessToken"] = token,
            // 官方文档要求 deviceSerial 字母大写
            ["deviceSerial"] = deviceSerial.Trim().ToUpperInvariant(),
        };
        using var doc = await PostFormRawAsync("/api/lapp/device/encrypt/off", form, ct);
        var root = doc.RootElement;
        // 60016 = 加密未开启，无需关闭 → 幂等成功，跳过 EnsureSuccess 的抛异常逻辑
        if (GetCode(root) == "60016")
        {
            _logger.LogInformation("萤石设备 {DeviceSerial} 加密未开启，关闭操作按幂等成功处理", deviceSerial);
            return;
        }
        EnsureSuccess(root);
    }

    // ── 视频编码切换（v3：header 鉴权 + form 参数 + meta.code 信封） ─────────────

    /// <summary>
    /// 远程把设备视频编码改为 H264/H265。
    /// 【设备侧配置变更，需调用方显式确认】H265 摄像头在浏览器 HLS 下无法软解，
    /// 改为 H264 可一劳永逸解决「编码非H264」错误图。
    /// v3 接口与既有 v2/lapp 接口不同：accessToken/deviceSerial/localIndex 走 header，
    /// encodeType/streamType 走 form，返回信封是 meta.code（不是顶层 code）。
    /// </summary>
    public async Task SetVideoEncodeAsync(string deviceSerial, string encodeType, int channelNo = 1, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(deviceSerial))
            throw new EzvizException("deviceSerial 不能为空", "10001");
        if (encodeType != "H264" && encodeType != "H265")
            throw new EzvizException("encodeType 仅支持 H264 或 H265", "10032");
        // 所有权校验：与 CloseEncryptionAsync / GetLiveAddressAsync 保持同一租户策略（超管可操作任意设备）
        if (await ResolveTenantCameraAsync(deviceSerial, ct) == null && !IsSuperAdmin())
            throw new EzvizException("无权访问该摄像头（不属于当前租户）", "40301");

        var token = await GetAccessTokenAsync(ct);
        var ch = Math.Max(1, channelNo);
        var headers = new Dictionary<string, string>
        {
            ["accessToken"] = token,
            // 官方文档要求 deviceSerial 字母大写
            ["deviceSerial"] = deviceSerial.Trim().ToUpperInvariant(),
            ["localIndex"] = ch.ToString(CultureInfo.InvariantCulture),
        };
        var form = new Dictionary<string, string>
        {
            ["encodeType"] = encodeType,
            ["streamType"] = "1", // 1=主码流
        };
        _logger.LogInformation("EZVIZ 切换视频编码 device={Device} ch={Ch} encode={Encode}", deviceSerial, ch, encodeType);
        using var doc = await SendWithHeadersRawAsync(HttpMethod.Put, "/api/v3/device/video/encodeType", headers, form, null, ct);
        // 关键：v3 返回 {"meta":{"code":200,...}}，必须读 meta.code，顶层 code 字段不存在
        EnsureV3Success(doc.RootElement);
    }

    /// <summary>
    /// 查询设备当前视频编码（v3 das/device/video/encode）。
    /// 返回 videoCode：1=标准H264，5=标准H265；查不到返回 null。可用于切换前确认/切换后验证。
    /// </summary>
    public async Task<int?> GetVideoEncodeAsync(string deviceSerial, int channelNo = 1, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(deviceSerial))
            throw new EzvizException("deviceSerial 不能为空", "10001");
        if (await ResolveTenantCameraAsync(deviceSerial, ct) == null && !IsSuperAdmin())
            throw new EzvizException("无权访问该摄像头（不属于当前租户）", "40301");

        var token = await GetAccessTokenAsync(ct);
        var ch = Math.Max(1, channelNo);
        var headers = new Dictionary<string, string>
        {
            ["accessToken"] = token,
            ["deviceSerial"] = deviceSerial.Trim().ToUpperInvariant(),
            ["channelNo"] = ch.ToString(CultureInfo.InvariantCulture),
        };
        var query = new Dictionary<string, string> { ["streamType"] = "1" };
        using var doc = await SendWithHeadersRawAsync(HttpMethod.Get, "/api/v3/das/device/video/encode", headers, null, query, ct);
        var root = doc.RootElement;
        EnsureV3Success(root);
        return GetInt(GetData(root), "videoCode");
    }

    // ── 设备状态 ──────────────────────────────────────────────────────────────

    public async Task<EzvizDeviceStatusDto> GetDeviceStatusAsync(string deviceSerial, CancellationToken ct = default)
    {
        if (await ResolveTenantCameraAsync(deviceSerial, ct) == null && !IsSuperAdmin())
            throw new EzvizException("无权访问该摄像头（不属于当前租户）", "40301");
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
        // 仅在本次显式填写验证码时覆盖，避免重新绑定（如只改区域、验证码留空）把已有验证码清空，
        // 导致加密设备再次 60019。
        if (!string.IsNullOrWhiteSpace(request.EwCode)) existing.EncryptionCode = request.EwCode;
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
    /// v3 接口底层：accessToken/deviceSerial 等走 header，参数走 form 或 query。
    /// 与 PostFormRawAsync 共用 HttpClient("Ezviz")、超时、JSON 与 HTTP 错误处理风格，
    /// 不改动任何既有方法。
    /// </summary>
    private async Task<JsonDocument> SendWithHeadersRawAsync(
        HttpMethod method,
        string path,
        Dictionary<string, string> headers,
        Dictionary<string, string>? form,
        Dictionary<string, string>? query,
        CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient("Ezviz");
        client.BaseAddress = new Uri(Opt.BaseUrl);
        client.Timeout = TimeSpan.FromSeconds(Math.Max(1, Opt.TimeoutSeconds));
        using var request = new HttpRequestMessage(method, BuildUri(path, query));
        foreach (var kv in headers)
            request.Headers.TryAddWithoutValidation(kv.Key, kv.Value);
        // FormUrlEncodedContent 自动设置 Content-Type: application/x-www-form-urlencoded
        if (form != null) request.Content = new FormUrlEncodedContent(form);

        using var resp = await client.SendAsync(request, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
        {
            _logger.LogWarning("萤石 HTTP {Status} {Method} {Path}: {Body}", (int)resp.StatusCode, method, path, Truncate(body));
            // 非 2xx 也可能带 v3 业务信封，优先给出准确错误码而非笼统 HTTP 状态
            var meta = ReadMeta(body);
            var detail = meta.Message ?? $"HTTP {(int)resp.StatusCode}";
            throw new EzvizException(
                meta.Code != null ? $"萤石接口错误 [{meta.Code}]：{detail}" : $"萤石网关返回 HTTP {(int)resp.StatusCode}",
                meta.Code ?? "HTTP_" + (int)resp.StatusCode);
        }
        try
        {
            return JsonDocument.Parse(body);
        }
        catch (JsonException)
        {
            _logger.LogWarning("萤石响应非 JSON {Method} {Path}: {Body}", method, path, Truncate(body));
            throw new EzvizException("萤石网关响应格式异常", "INVALID_JSON");
        }
    }

    private static string BuildUri(string path, IReadOnlyDictionary<string, string>? query)
    {
        if (query == null || query.Count == 0) return path;
        var parts = new List<string>(query.Count);
        foreach (var kv in query)
            parts.Add(Uri.EscapeDataString(kv.Key) + "=" + Uri.EscapeDataString(kv.Value));
        return path + "?" + string.Join("&", parts);
    }

    /// <summary>从响应体尝试解析 v3 的 meta.code / meta.message（非 2xx 时也适用）。</summary>
    private static (string? Code, string? Message) ReadMeta(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            return (GetMetaCode(doc.RootElement), GetMetaMessage(doc.RootElement));
        }
        catch (JsonException) { return (null, null); }
    }

    /// <summary>
    /// 常见萤石错误码的中文解释（文档「错误码」章节 + 真机实测）。
    /// 目的：账号权限/套餐类问题在界面上一眼可懂，而不是抛原始英文或一串数字。
    /// </summary>
    private static readonly Dictionary<string, string> ErrorHints = new(StringComparer.Ordinal)
    {
        ["20018"] = "当前账号不拥有该设备（多为被分享设备，直播/抓拍/状态接口被拒）",
        ["20011"] = "设备不支持视频编码切换或设备异常",
        ["20008"] = "设备响应超时，请稍后重试",
        ["10032"] = "参数错误或无该设备操作权限",
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
        ["60019"] = "设备已开启视频加密，请在绑定摄像头时录入设备验证码(ewcode)，可在萤石APP-设备-安全中查看",
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

    /// <summary>
    /// v3 接口成功判定：优先读 meta.code，legacy 顶层 code 仅作兜底兼容。
    /// 萤石 v3 信封是 {"meta":{"code":200,"message":"操作成功","moreInfo":{}}}，
    /// 顶层没有 code 字段——误用 GetCode 会把成功判成失败（code 为 null）。
    /// </summary>
    private static void EnsureV3Success(JsonElement root)
    {
        var code = GetMetaCode(root) ?? GetCode(root);
        if (code == "200") return;
        var msg = GetMetaMessage(root) ?? GetString(root, "message") ?? GetString(root, "msg") ?? "萤石接口调用失败";
        var hint = code != null && ErrorHints.TryGetValue(code, out var h) ? "（" + h + "）" : string.Empty;
        throw new EzvizException($"萤石接口错误 [{code ?? "?"}]：{msg}{hint}", code);
    }

    private static string? GetMetaCode(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("meta", out var meta) && meta.ValueKind == JsonValueKind.Object)
            return GetString(meta, "code"); // GetString 兼容 code 为数字 200 的情况
        return null;
    }

    private static string? GetMetaMessage(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("meta", out var meta) && meta.ValueKind == JsonValueKind.Object)
            return GetString(meta, "message");
        return null;
    }

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
