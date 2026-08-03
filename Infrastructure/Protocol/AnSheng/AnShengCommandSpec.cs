namespace IoTPlatform.Infrastructure.Protocol.AnSheng;

/// <summary>
/// 命令方向。
/// </summary>
public enum AnShengCommandDirection
{
    /// <summary>平台下发 → 设备应答。</summary>
    Downlink = 0,

    /// <summary>设备主动上行（事件），平台不可下发。</summary>
    Uplink = 1
}

/// <summary>
/// 安圣二开协议单条命令的规格说明（对应 asopen.md 中一个 <c>##</c> 小节）。
/// </summary>
public sealed class AnShengCommandSpec
{
    /// <summary>协议方法名，如 <c>getDevInfo</c> / <c>action</c>。</summary>
    public string Method { get; init; } = string.Empty;

    /// <summary>中文标题，如「插槽开关动作」。</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>命令方向。</summary>
    public AnShengCommandDirection Direction { get; init; } = AnShengCommandDirection.Downlink;

    /// <summary>支持该命令的设备品类位掩码。</summary>
    public AnShengDeviceCapability SupportedKinds { get; init; } = AnShengDeviceCapability.None;

    /// <summary>命令参数规格列表（平铺在 JSON 顶层，不含 method/frameId/imei/timestamp 等公共字段）。</summary>
    public IReadOnlyList<AnShengParamSpec> Params { get; init; } = Array.Empty<AnShengParamSpec>();

    /// <summary>是否为设备主动上报事件（平台不可下发）。</summary>
    public bool IsEvent { get; init; }

    /// <summary>协议文档是否标注为「测试中」。</summary>
    public bool IsBeta { get; init; }

    /// <summary>整条命令要求的最低固件版本；为 null 表示无限制。</summary>
    public string? MinFirmware { get; init; }

    /// <summary>协议文档锚点（<c>##</c> 小节标题），便于回溯。</summary>
    public string DocAnchor { get; init; } = string.Empty;

    /// <summary>
    /// 判断指定品类是否支持本命令。
    /// </summary>
    /// <param name="kind">设备品类。<see cref="AnShengDeviceKind.Unknown"/> 视为「无法判定」，返回 true 以免误拦截。</param>
    /// <returns>支持返回 true。</returns>
    public bool IsSupportedBy(AnShengDeviceKind kind)
    {
        if (kind == AnShengDeviceKind.Unknown) return true;
        return (SupportedKinds & kind.ToCapability()) != AnShengDeviceCapability.None;
    }

    /// <summary>
    /// 按参数规格校验一组下发参数。
    /// </summary>
    /// <param name="parameters">平铺参数字典，可为 null。</param>
    /// <param name="firmware">设备当前固件版本串，可为 null（不做版本校验）。</param>
    /// <param name="allowUnknownParams">是否允许传入规格外的参数；默认 true（协议后续可能新增字段）。</param>
    /// <returns>校验结果。</returns>
    public AnShengValidationResult ValidateParams(
        IReadOnlyDictionary<string, object?>? parameters,
        string? firmware = null,
        bool allowUnknownParams = true)
    {
        var errors = new List<string>();

        if (IsEvent)
        {
            errors.Add($"{Method} 是设备上报事件，平台不可下发");
            return AnShengValidationResult.Fail(errors);
        }

        if (!AnShengFirmwareVersion.Satisfies(firmware, MinFirmware))
        {
            errors.Add($"{Method} 要求固件版本 >= {MinFirmware}，当前 {firmware}");
        }

        foreach (var spec in Params)
        {
            object? actual = null;
            if (parameters != null && parameters.TryGetValue(spec.Name, out var found))
            {
                actual = found;
            }

            if (actual != null && !AnShengFirmwareVersion.Satisfies(firmware, spec.MinFirmware))
            {
                errors.Add($"参数 {spec.Name} 要求固件版本 >= {spec.MinFirmware}，当前 {firmware}");
                continue;
            }

            if (!spec.Validate(actual, out var error) && error != null)
            {
                errors.Add(error);
            }
        }

        if (!allowUnknownParams && parameters != null)
        {
            var known = Params.Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
            foreach (var key in parameters.Keys.Where(k => !known.Contains(k)))
            {
                errors.Add($"未知参数 {key}（{Method} 不支持）");
            }
        }

        return errors.Count == 0 ? AnShengValidationResult.Ok : AnShengValidationResult.Fail(errors);
    }

    /// <inheritdoc />
    public override string ToString() => $"{Method}（{Title}）";
}
