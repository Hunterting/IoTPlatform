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

    /// <summary>
    /// <b>设备应答报文</b>中需要打码的敏感字段名（T7 决策 D3 补全）。
    ///
    /// 【为什么不能只靠 <see cref="Params"/> 推导】
    ///   <c>getMqtt</c> 是一条<b>无参数</b>命令 —— 口令只出现在<b>应答</b>里，下发侧一个字段都没有。
    ///   若敏感字段集只从 <see cref="Params"/> 收集，<c>SecretFieldNames("getMqtt")</c> 恒为空集，
    ///   设备回读的 <c>password</c> 就会原样落进 <c>AnShengCommandRecord.ResponseJson</c>，
    ///   再经 <c>GET /api/v1/ansheng/commands/{id}</c> 明文吐给前端。
    ///   这正是 <c>AnShengSecretMasker</c> 自己的注释所声称要堵死的那扇后门
    ///   （「只掩下行不掩上行，等于留了一扇后门」）。
    ///
    /// 【与 <c>AnShengParamSpec.SecretFields</c> 的分工】
    ///   前者描述「我发出去的参数里哪些是机密」，本属性描述「设备回给我的报文里哪些是机密」。
    ///   两者<b>共用同一份常量</b>（<c>AnShengCommandCatalog.MqttSecretFields</c>），
    ///   保证协议新增机密字段时只有一处需要改。
    ///
    /// 空集合表示该命令的应答无需额外掩码（绝大多数命令如此）。
    /// </summary>
    public IReadOnlyList<string> ResponseSecretFields { get; init; } = Array.Empty<string>();

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
    /// 按参数规格校验一组下发参数（文案版，T7 之前的既有签名）。
    ///
    /// 内部委托结构化重载，只把 <see cref="AnShengParamViolation"/> 降级成中文串。
    /// 既有单元测试（<c>AnShengProtocolConformanceTests</c> 等）大量依赖本签名，<b>不得改名或改语义</b>。
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
        var valid = ValidateParams(parameters, firmware, allowUnknownParams, out var violations);
        if (valid)
        {
            return AnShengValidationResult.Ok;
        }

        return AnShengValidationResult.Fail(violations.Select(v => v.Message).ToArray());
    }

    /// <summary>
    /// 按参数规格校验一组下发参数，失败时给出<b>结构化</b>违规清单（T7 新增，Guard 专用）。
    ///
    /// 【与文案版的唯一区别】违规带 <see cref="AnShengParamViolationKind"/> 与 <c>MinFirmware</c>，
    /// 使调用方能把「固件不足」与「参数不合法」判成<b>两种</b>拒绝原因
    /// （验收 #2 断言 <c>RejectedByValidation</c>、验收 #3 断言 <c>RejectedByFirmware</c>）。
    ///
    /// 【校验顺序】上行事件 → 命令级固件 → 逐参数（先固件后规格）→ 未知参数。
    /// 参数级固件不足时<b>跳过</b>该参数的其余规格校验：固件都不支持这个参数了，
    /// 再报「类型不对」只会淹没真正的原因。
    /// </summary>
    /// <param name="parameters">平铺参数字典，可为 null。</param>
    /// <param name="firmware">设备当前固件版本串；null / 无法解析时<b>放行</b>固件校验（不误拦截）。</param>
    /// <param name="allowUnknownParams">是否允许传入规格外的参数。</param>
    /// <param name="violations">全部违规；通过时为空集合，永不为 null。</param>
    /// <returns>全部通过返回 true。</returns>
    public bool ValidateParams(
        IReadOnlyDictionary<string, object?>? parameters,
        string? firmware,
        bool allowUnknownParams,
        out IReadOnlyList<AnShengParamViolation> violations)
    {
        var found = new List<AnShengParamViolation>();

        if (IsEvent)
        {
            found.Add(new AnShengParamViolation(
                string.Empty,
                AnShengParamViolationKind.NotDownlinkable,
                $"{Method} 是设备上报事件，平台不可下发"));
            violations = found;
            return false;
        }

        if (!AnShengFirmwareVersion.Satisfies(firmware, MinFirmware))
        {
            found.Add(new AnShengParamViolation(
                string.Empty,
                AnShengParamViolationKind.FirmwareTooLow,
                $"{Method} 要求固件版本 >= {MinFirmware}，当前 {firmware}",
                MinFirmware));
        }

        foreach (var spec in Params)
        {
            object? actual = null;
            if (parameters != null && parameters.TryGetValue(spec.Name, out var supplied))
            {
                actual = supplied;
            }

            // 只有「真的传了这个参数」才谈得上固件门槛；没传就与固件无关。
            if (actual != null && !AnShengFirmwareVersion.Satisfies(firmware, spec.MinFirmware))
            {
                found.Add(new AnShengParamViolation(
                    spec.Name,
                    AnShengParamViolationKind.FirmwareTooLow,
                    $"参数 {spec.Name} 要求固件版本 >= {spec.MinFirmware}，当前 {firmware}",
                    spec.MinFirmware));
                continue;
            }

            if (!spec.ValidateDetailed(actual, out var violation) && violation != null)
            {
                found.Add(violation);
            }
        }

        if (!allowUnknownParams && parameters != null)
        {
            var known = Params.Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
            foreach (var key in parameters.Keys.Where(k => !known.Contains(k)))
            {
                found.Add(new AnShengParamViolation(
                    key,
                    AnShengParamViolationKind.UnknownParam,
                    $"未知参数 {key}（{Method} 不支持）"));
            }
        }

        violations = found;
        return found.Count == 0;
    }

    /// <inheritdoc />
    public override string ToString() => $"{Method}（{Title}）";
}
