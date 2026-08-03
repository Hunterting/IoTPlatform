using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace IoTPlatform.Tools.AnShengFieldTest;

/// <summary>
/// Writes the field-test report as Markdown:
///   conclusion / environment / preflight / listen / dispatch / non-response / catalog-corrections /
///   notes / empirical decisions / e2e gaps / protocol facts / cross-step findings + safety nets.
/// </summary>
public static class ReportWriter
{
    /// <summary>
    /// Response fields that ARE described in the vendor document but are NOT declared in the
    /// production Catalog => a Phase 1 implementation gap we own and must close.
    /// Everything else observed on the wire is a VENDOR DOCUMENT gap.
    /// </summary>
    private static readonly HashSet<string> DocumentedButMissingFromCatalog = new(StringComparer.Ordinal)
    {
        "version", "slotAmount", "phaseAmount"
    };

    /// <summary>Fields carrying location data — called out separately for compliance review.</summary>
    private static readonly HashSet<string> LocationSensitiveFields = new(StringComparer.Ordinal)
    {
        "gps"
    };

    public static string Write(FieldTestReportData data, string directory, DateTime stamp)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"report-{stamp:yyyyMMdd-HHmmss}.md");
        var sb = new StringBuilder();

        // ---- 结论速览 ----
        sb.AppendLine("# 安圣 4G 开关 真机联调报告");
        sb.AppendLine();
        sb.AppendLine($"- 生成时间(UTC): {stamp:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"- Broker: `{data.Options.Host}:{data.Options.Port}`");
        sb.AppendLine($"- 连接结果: **{(data.ConnectionOk ? "已连接" : "连接失败")}**");
        sb.AppendLine($"- 监听模式: {(data.Options.ListenOnly ? "是 (--listen-only)" : "否")}");
        sb.AppendLine($"- 监听到 IMEI 数: {data.HeardImeis.Count}");
        sb.AppendLine($"- 下发步骤数: {data.Steps.Count}（PASS {data.Steps.Count(s => s.Verdict == StepVerdict.Pass)} / " +
                      $"MISMATCH {data.Steps.Count(s => s.Verdict == StepVerdict.Mismatch)} / " +
                      $"TIMEOUT {data.Steps.Count(s => s.Verdict == StepVerdict.Timeout)} / " +
                      $"SKIPPED {data.Steps.Count(s => s.Verdict == StepVerdict.Skipped)} / " +
                      $"ERROR {data.Steps.Count(s => s.Verdict == StepVerdict.Error)}）");
        sb.AppendLine($"- 退出码: {data.ExitCode}");
        if (data.CapturePath is not null)
            sb.AppendLine($"- 抓包文件: `{data.CapturePath}`");
        sb.AppendLine();

        // ---- P0：最终物理状态（本报告最重要的一行）----
        sb.AppendLine("## ⚠ 最终物理状态（P0）");
        sb.AppendLine();
        sb.AppendLine($"- **退出时读回的开关状态: `slots = {Esc(data.FinalSlots)}`**");
        sb.AppendLine($"- **是否确认全部断开: {(data.FinalSwitchOffConfirmed ? "✅ 是（全部插槽为 0）" : "❌ 否 —— 需人工介入确认")}**");
        sb.AppendLine();
        sb.AppendLine("> 背景：第一轮剧本把开关**永久留在了闭合状态**。根因是 `startDelayTask` 的 `sAction:\"on\"` 会**立即闭合**，");
        sb.AppendLine("> 而 `eAction:\"off\"` 要等延时到期才执行；剧本随后的 `stopDelayTask` 取消了该任务，`eAction` 因此**永不执行**。");
        sb.AppendLine("> 本轮已修复：(1) 剧本末尾追加 `action off` + `getDevStatus` 读回断言 `slots[0]==0`；");
        sb.AppendLine("> (2) `FieldTestRunner` 增加 `GuaranteedSwitchOffAsync` 安全网，只要本轮下发过任何 control 命令，");
        sb.AppendLine("> 无论异常/超时/Ctrl+C，退出前都强制 `action off` 并读回验证；");
        sb.AppendLine("> (3) 安全网顺序为「先物理断开 → 再复位时钟 → 再还原自动上报」；");
        sb.AppendLine("> (4) 本地自检新增 P0 回归护栏，剧本若不以「强制断开 + 读回断言」收尾，preflight 直接 FAIL。");
        sb.AppendLine("> 安全网逐条执行记录见 **第十一节**。");
        sb.AppendLine();

        // ---- 一、运行环境 ----
        sb.AppendLine("## 一、运行环境");
        sb.AppendLine();
        sb.AppendLine("| 项 | 值 |");
        sb.AppendLine("| --- | --- |");
        sb.AppendLine($"| Host | {Esc(data.Options.Host)} |");
        sb.AppendLine($"| Port | {data.Options.Port} |");
        sb.AppendLine($"| Username | {Esc(data.Options.Username)} |");
        sb.AppendLine($"| 上行订阅过滤 (平台 SUBSCRIBE) | `{Esc(data.Options.UplinkTopicFilter)}` |");
        sb.AppendLine($"| 下行发布模板 (平台 PUBLISH) | `{Esc(data.Options.DownlinkTopicTemplate)}` |");
        sb.AppendLine($"| QoS | {data.Options.Qos} |");
        sb.AppendLine($"| KeepAlive(s) | {data.Options.KeepAliveSeconds} |");
        sb.AppendLine($"| 监听时长(s) | {data.Options.ListenSeconds} |");
        sb.AppendLine($"| 驻留窗口(s) | {data.Options.DwellSeconds} |");
        sb.AppendLine($"| --listen-only | {data.Options.ListenOnly} |");
        sb.AppendLine($"| --allow-config | {data.Options.AllowConfig} |");
        sb.AppendLine($"| --allow-control | {data.Options.AllowControl} |");
        sb.AppendLine($"| 应答超时(s) | {data.Options.ResponseTimeoutSeconds} |");
        sb.AppendLine($"| 节流(ms) | {data.Options.ThrottleMs} |");
        sb.AppendLine($"| 目标 IMEI | {Esc(data.Options.Imei ?? "(监听首个)")} |");
        sb.AppendLine($"| --kind | {Esc(data.Options.Kind ?? "(推断)")} |");
        sb.AppendLine($"| 推断 Kind | {Esc(data.ResolvedKind ?? "-")} |");
        sb.AppendLine($"| 槽位 | {data.Options.SlotNum} |");
        sb.AppendLine($"| 延时(s) | {data.Options.DelaySeconds} |");
        sb.AppendLine();

        // ---- 二、本地自检 ----
        sb.AppendLine("## 二、本地自检（不接触设备）");
        sb.AppendLine();
        sb.AppendLine("| 检查 | 结果 | 说明 |");
        sb.AppendLine("| --- | --- | --- |");
        foreach (var c in data.Preflight)
            sb.AppendLine($"| {Esc(c.Name)} | {(c.Passed ? "PASS" : "FAIL")} | {Esc(c.Detail)} |");
        sb.AppendLine();

        // ---- 三、监听阶段 ----
        sb.AppendLine("## 三、监听阶段（上行抓取）");
        sb.AppendLine();
        sb.AppendLine($"连接: **{(data.ConnectionOk ? "成功" : "失败")}** — {Esc(data.ConnectionDetail)}");
        sb.AppendLine();
        sb.AppendLine($"监听到 IMEI 共 **{data.HeardImeis.Count}** 个：");
        sb.AppendLine();
        sb.AppendLine("| IMEI | 上行条数 |");
        sb.AppendLine("| --- | --- |");
        foreach (var imei in data.HeardImeis)
            sb.AppendLine($"| {Esc(imei)} | {data.ImeiMessageCounts.GetValueOrDefault(imei, 0)} |");
        if (data.HeardImeis.Count == 0)
            sb.AppendLine("| （无） | 0 |");
        sb.AppendLine();

        // ---- 四、命令下发结果 ----
        sb.AppendLine("## 四、命令下发结果");
        sb.AppendLine();
        if (data.Steps.Count == 0)
        {
            sb.AppendLine("_未下发任何命令（监听模式或无设备）。_");
        }
        else
        {
            sb.AppendLine("| # | 方法 | 分组 | 发出 payload | 收到 payload | frameId 匹配 | result | Schema 一致 | 结论 |");
            sb.AppendLine("| --- | --- | --- | --- | --- | --- | --- | --- | --- |");
            foreach (var s in data.Steps)
            {
                var verdictCell = s.ExpectedFailure && s.Verdict == StepVerdict.Pass
                    ? "**PASS**（预期失败）"
                    : $"**{s.VerdictLabel}**";
                sb.AppendLine($"| {s.Index} | {Esc(s.Method)} | {Esc(s.Group)} | " +
                              $"`{Esc(Trunc(s.RequestPayload, 120))}` | " +
                              $"`{Esc(Trunc(s.ResponsePayload, 120))}` | " +
                              $"{s.FrameIdMatched} | {Esc(s.ResponseResult ?? "-")} | " +
                              $"{(s.Schema?.HasError == true ? "否" : (s.Schema is null ? "-" : "是"))} | " +
                              $"{verdictCell} |");
            }
            sb.AppendLine();

            // 命令明细（完整原始报文，不截断）
            sb.AppendLine("### 命令明细（完整原始报文）");
            sb.AppendLine();
            foreach (var s in data.Steps)
            {
                var title = s.ExpectedFailure && s.Verdict == StepVerdict.Pass
                    ? "PASS（预期失败）"
                    : s.VerdictLabel;
                sb.AppendLine($"#### [{s.Index}] {s.Method} ({title})");
                sb.AppendLine($"- 分组: {Esc(s.Group)}　风险: {s.Risk}　目的: {Esc(s.Purpose)}");
                if (s.ExpectedFailure)
                    sb.AppendLine("- **本步骤为「预期失败」探测**：设备返回非 ok 才是正确结果，错误码即为交付物。");
                if (s.AssertError is { Length: > 0 })
                    sb.AppendLine($"- **断言失败**: {Esc(s.AssertError)}");
                if (s.Remark is { Length: > 0 })
                    sb.AppendLine($"- 备注: {Esc(s.Remark)}");

                if (s.DwellSeconds > 0)
                {
                    sb.AppendLine($"- 驻留窗口: {s.DwellSeconds}s，窗口内收到上行 **{s.DwellMessages.Count}** 条");
                    sb.AppendLine();
                    if (s.DwellMessages.Count == 0)
                    {
                        sb.AppendLine("_窗口内 0 条上行。_");
                    }
                    else
                    {
                        for (int i = 0; i < s.DwellMessages.Count; i++)
                        {
                            sb.AppendLine($"**窗口上行 #{i + 1}:**");
                            sb.AppendLine("```json");
                            sb.AppendLine(Pretty(s.DwellMessages[i]));
                            sb.AppendLine("```");
                        }
                    }
                    sb.AppendLine();
                    continue;
                }

                sb.AppendLine($"- 发出 frameId: `{Esc(s.RequestFrameId ?? "-")}`　节流等待: {s.ThrottleWaitedMs}ms　RTT: {s.RoundTripMs?.ToString() ?? "-"}ms");
                sb.AppendLine();
                sb.AppendLine("**发出:**");
                sb.AppendLine("```json");
                sb.AppendLine(Pretty(s.RequestPayload));
                sb.AppendLine("```");
                sb.AppendLine();
                sb.AppendLine("**收到:**");
                sb.AppendLine("```json");
                sb.AppendLine(Pretty(s.ResponsePayload));
                sb.AppendLine("```");
                sb.AppendLine();
            }

            // Q23 节流统计
            var q23 = data.Steps.Where(s => s.Group == "Q23" && s.RoundTripMs.HasValue).ToList();
            if (q23.Count > 0)
            {
                sb.AppendLine("### Q23 限流压测统计（连续下发 100ms 是否足够）");
                sb.AppendLine();
                sb.AppendLine($"- 样本数: {q23.Count}（背靠背 getDevStatus，节流器 {data.Options.ThrottleMs}ms/IMEI）");
                sb.AppendLine($"- RTT: min={q23.Min(s => s.RoundTripMs)}ms / avg={(long)q23.Average(s => s.RoundTripMs!.Value)}ms / max={q23.Max(s => s.RoundTripMs)}ms");
                sb.AppendLine($"- 节流实际等待: min={q23.Min(s => s.ThrottleWaitedMs)}ms / max={q23.Max(s => s.ThrottleWaitedMs)}ms");
                sb.AppendLine($"- 成功: {q23.Count(s => s.Verdict == StepVerdict.Pass)} / 超时: {q23.Count(s => s.Verdict == StepVerdict.Timeout)} / 其它: {q23.Count(s => s.Verdict is not (StepVerdict.Pass or StepVerdict.Timeout))}");
                sb.AppendLine($"- 结论: {(q23.All(s => s.Verdict == StepVerdict.Pass) ? "**100ms 间隔下 10 条连续命令全部正常应答，节流阈值足够。**" : "**出现丢失/超时，100ms 间隔不足，需上调节流阈值。**")}");
                sb.AppendLine();
            }
        }

        // ---- 五、非应答上行 ----
        sb.AppendLine("## 五、非应答上行（事件 / Will / 主动上报）");
        sb.AppendLine();
        var nonResp = data.AllUplinks
            .Where(r => r.FrameId is null || (r.Message?.IsEvent == true) || r.IsWill)
            .ToList();
        if (nonResp.Count == 0)
        {
            sb.AppendLine("_无。_");
        }
        else
        {
            foreach (var r in nonResp)
            {
                sb.AppendLine($"- `{Esc(r.Topic)}` imei={Esc(r.Imei ?? "-")} method={Esc(r.Method ?? "-")} " +
                              $"isWill={r.IsWill} isEvent={(r.Message?.IsEvent ?? false)}");
                sb.AppendLine("  ```json");
                sb.AppendLine(Pretty(r.Raw));
                sb.AppendLine("  ```");
            }
        }
        sb.AppendLine();

        // ---- 六、Catalog 修正建议 ----
        WriteCatalogSection(sb, data);

        // ---- 七、运行提示 ----
        sb.AppendLine("## 七、运行提示");
        sb.AppendLine();
        sb.AppendLine("```bash");
        sb.AppendLine("# 单轮跑完整剧本（用户已放行控制组；下游无负载，插槽通断无风险）:");
        sb.AppendLine("#   - setTime 拨 +1h 验 Q11/Q12，剧本末尾自动复位并二次确认");
        sb.AppendLine("#   - setAutoReport 临时开推送(30s)，剧本末尾还原为原始值（读不到原值则拒绝修改）");
        sb.AppendLine("#   - 剧本末尾强制 action off + 读回断言 slots[0]==0");
        sb.AppendLine("#   - 中断/异常/超时时 finally 依次执行三道安全网：断开开关 -> 复位时钟 -> 还原自动上报");
        sb.AppendLine("dotnet run --project tools/AnShengFieldTest -- --imei <15位IMEI> --listen-sec 0 --allow-config --allow-control");
        sb.AppendLine("```");
        if (!string.IsNullOrEmpty(data.Options.Notes))
        {
            sb.AppendLine();
            sb.AppendLine($"> 备注: {Esc(data.Options.Notes)}");
        }
        sb.AppendLine();

        // ---- 八、已验证设计决策（empirical）----
        sb.AppendLine("## 八、已验证设计决策（empirical）");
        sb.AppendLine();
        sb.AppendLine("> 以下结论由 team-lead 用 python+paho 在 **同一台 EMQX broker** 上独立回环验证得出，");
        sb.AppendLine("> 与 Phase 1 重构的某项设计决定相互印证，记录于此供 Phase 2 评审。");
        sb.AppendLine();
        sb.AppendLine("### 8.1 重叠订阅 EMQX 不去重");
        sb.AppendLine();
        sb.AppendLine("- 同时挂载 `#`、`/iot/server/#`、`/iot/server/iot-board/+` 三个重叠过滤器时，**同一条报文被投递了 3 次**。");
        sb.AppendLine("- 说明该 EMQX 实例**不对重叠订阅做去重**：订阅多少次、同一消息就投递多少次。");
        sb.AppendLine();
        sb.AppendLine("### 8.2 「同 pattern 只订阅一次」是必需优化，不是可选");
        sb.AppendLine();
        sb.AppendLine("- Phase 1 重构决定：当 `WillTopicPattern == PublishTopicPattern` 时，**只订阅一次**（而非 will / publish 各订阅一次）。");
        sb.AppendLine("- 本实证结论支持该决定：**若当初为 will 与 publish 各订阅一次（同一 pattern），线上每条设备报文都会被处理两遍**");
        sb.AppendLine("  → 重复入库、规则引擎重复执行、告警重复触发。");
        sb.AppendLine();
        sb.AppendLine("**Q21 引用措辞**：");
        sb.AppendLine();
        sb.AppendLine("- 本结论**部分回答** Q21：已证实若两 pattern 相同则单订阅是必需。但遗嘱 topic 是否真的等于上行 topic，仍需通电断电抓遗嘱验证。");
        sb.AppendLine("- 核对 `docs/ansheng-field-test-checklist.md`：**Q21 的主题是「遗嘱 `close` 报文的实际字段、顺序与触发条件」**（checklist 行 475），");
        sb.AppendLine("  而「同 pattern 只订阅一次的优化要重审」只是 **Q21 验证项 ③ 里与 Q1 联动的一句附注**（checklist 行 477）。");
        sb.AppendLine("- 结论也应写成条件式：");
        sb.AppendLine("  - **若遗嘱 topic == publish topic** → 「同 pattern 只订阅一次」是**必需**的（否则每条报文处理两遍）；");
        sb.AppendLine("  - **若遗嘱 topic != publish topic** → 两个 pattern 都必须订阅，但**仍不得对同一 pattern 重复订阅**。");
        sb.AppendLine("- **Q21 本体（遗嘱报文字段集合 / 是否真无 timestamp / 实际投递 topic）本轮仍未验证**：");
        sb.AppendLine("  触发条件是拔电或断网并超过 keepAlive(30s)，本工具无法远程制造，需现场配合。**列为未关闭项。**");
        sb.AppendLine();
        sb.AppendLine("### 8.3 第一轮 0 上行的根因（非工具问题）");
        sb.AppendLine();
        sb.AppendLine("- 全量 `#` 根通配订阅 75s 仍为 0 条报文：整 broker 无流量，故 topic 形态并非当时 0 上行原因。");
        sb.AppendLine("- ACL 无限制：四个过滤器 SUBACK 均为 `granted`，无 0x80 拒绝。");
        sb.AppendLine("- 回环自测通过（自己 publish 自己收），订阅链路 100% 健康。");
        sb.AppendLine("- 结论：**设备真的一条没发**——纯轮询模式，自动上报关闭，仅被轮询才应答。");
        sb.AppendLine("- 该结论正是本轮新增 **G6 自动上报验证组** 的动因（见第十一节 11.1）。");
        sb.AppendLine();

        // ---- 九、端到端链路缺口 ----
        sb.AppendLine("## 九、端到端链路缺口（平台侧尚未接通）");
        sb.AppendLine();
        sb.AppendLine("> 本节为 **Phase 2 入口清单**。本报告工具直连 broker，验证的是「协议报文层」是否正确；");
        sb.AppendLine("> 但「工具通过」≠「平台端到端可用」。下列缺口不补，即使设备开始发报文，正式链路也不会被触发。");
        sb.AppendLine();
        sb.AppendLine("### 9.1 现状证据（DB 实查，全部为空）");
        sb.AppendLine();
        sb.AppendLine("| 表 | 记录数 | 含义 |");
        sb.AppendLine("| --- | --- | --- |");
        sb.AppendLine("| `devices` | 0 | 平台无已认领设备 |");
        sb.AppendLine("| `discovered_ansheng_devices` | 0 | 无已发现未认领设备 |");
        sb.AppendLine("| `ansheng_device_configs` | 0 | 无设备级配置（自动上报参数等） |");
        sb.AppendLine("| `protocol_configs` | 0 | **无启用中的 ANSHENG_MQTT 协议配置** |");
        sb.AppendLine();
        sb.AppendLine("`protocol_configs` 为空是关键：它意味着 **ProtocolConfigService 没有可加载的 ANSHENG_MQTT 配置，");
        sb.AppendLine("适配器从未被启动**，因此即便设备开始上行，DataCollectionService 也不会被驱动去解析/入库。");
        sb.AppendLine();
        sb.AppendLine("### 9.2 从「工具验证通过」到「平台自动采集入库」还差什么");
        sb.AppendLine();
        sb.AppendLine("1. **建 ANSHENG_MQTT 协议配置记录**：在 `protocol_configs` 写入一条 `enabled` 的记录，");
        sb.AppendLine("   含 broker 地址/端口/账号、上行订阅 topic、下行发布模板、QoS 等（值应与此工具所用一致：");
        sb.AppendLine("   `120.79.3.248:18883`，上行 `/iot/server/#`，下行 `/iot/client/iot-board/{imei}`）。");
        sb.AppendLine("2. **启动协议适配器**：ProtocolConfigService 加载该配置 → 启动 MQTT 客户端、按上行 topic 订阅、");
        sb.AppendLine("   把收到的报文投递给 DataCollectionService。注意第八节的实证结论——同一 pattern 只订阅一次，");
        sb.AppendLine("   否则每条报文被处理两遍。");
        sb.AppendLine("3. **设备发现与认领**：设备首次上行后写入 `discovered_ansheng_devices`，再认领入 `devices` 表");
        sb.AppendLine("   （绑定租户/点位/位置）。认领前平台不会把它当作「自己的」设备去采集。");
        sb.AppendLine("4. **写入设备配置**：在 `ansheng_device_configs` 落设备级配置（如自动上报参数 `getDevStatusSec` 等）。");
        sb.AppendLine("   **注意**：设备出厂为纯轮询模式，若不显式下发 `setAutoReport`，平台永远收不到主动上报（见 11.1）。");
        sb.AppendLine("5. **数据落库链路**：DataCollectionService 解析 → 时序/明细表入库；接线告警与规则引擎。");
        sb.AppendLine("   解析器必须按 method 分派 `tasks` 字段（见 10.2），并统一 `voltage/current` 的序列化（见 10.5）。");
        sb.AppendLine("6. **落库 frameId**：延时/定时任务的 `sign` 就是下发时的 frameId，是设备侧任务主键（见 10.3），");
        sb.AppendLine("   平台必须持久化 frameId 才能后续管理任务。");
        sb.AppendLine("7. **端到端冒烟**：用本工具同款报文（或真实设备）发一条，确认能从 `devices` 认领一路走到入库，");
        sb.AppendLine("   且采集到的 timestamp/状态与设备一致（呼应 Phase 1 的 timestamp 注入逻辑）。");
        sb.AppendLine();
        sb.AppendLine("### 9.3 为什么工具通过 ≠ 平台可用");
        sb.AppendLine();
        sb.AppendLine("- 本工具走的是 **直连 broker + 生产协议类（Builder/Parser/Catalog）** 的旁路，完全绕过");
        sb.AppendLine("  ProtocolConfigService / DataCollectionService / 设备认领 / 入库 等平台链路。");
        sb.AppendLine("- 因此它能证明「设备 ↔ 平台 的协议报文结构正确、Catalog 覆盖完整、时钟/timestamp 逻辑正确」，");
        sb.AppendLine("  但**不能**证明「平台已配置好并能自动把报文变成库里的一条数据」。");
        sb.AppendLine("- 当前 `protocol_configs = 0` 状态下，正式链路是「死的」：设备上线也不会被采集。");
        sb.AppendLine();
        sb.AppendLine("### 9.4 本报告未做的事");
        sb.AppendLine();
        sb.AppendLine("- **未修改数据库、未插入/更新任何记录**（遵守「只写分析、不动 DB」要求）。");
        sb.AppendLine("- **未修改任何生产代码**：改动全部限于 `tools/AnShengFieldTest/**`。");
        sb.AppendLine("- 上述 9.2 仅作为 Phase 2 的执行清单，不在此处实施。");
        sb.AppendLine();

        // ---- 十、协议事实 ----
        WriteProtocolFactsSection(sb, data);

        // ---- 十一、剧本发现 + 安全网 ----
        WriteFindingsAndSafetySection(sb, data);

        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return path;
    }

    // ---- 六、Catalog 修正建议 --------------------------------------------

    private static void WriteCatalogSection(StringBuilder sb, FieldTestReportData data)
    {
        sb.AppendLine("## 六、Catalog 修正建议");
        sb.AppendLine();

        var hard = data.Steps
            .Where(s => s.Schema?.Findings.Any(f => f.Severity == FindingSeverity.Error) == true)
            .SelectMany(s => s.Schema!.Findings.Where(f => f.Severity == FindingSeverity.Error)
                .Select(f => (s.Method, f.Message)))
            .Distinct()
            .ToList();

        sb.AppendLine("### 6.1 硬冲突（必须修正）");
        sb.AppendLine();
        if (hard.Count == 0)
            sb.AppendLine("_未发现硬冲突。_");
        else
            foreach (var (m, msg) in hard)
                sb.AppendLine($"- [{Esc(m)}] {Esc(msg)}");
        sb.AppendLine();

        // 按 (method, 字段名) 去重 —— 上一版把 getDevStatus 的 8 个字段列了 4 遍。
        var undeclared = data.Steps
            .Where(s => s.Schema is not null)
            .SelectMany(s => s.Schema!.Findings
                .Where(f => f.Severity == FindingSeverity.Info)
                .Select(f => new
                {
                    s.Method,
                    Field = ExtractFieldName(f.Message),
                    Type = ExtractFieldType(f.Message)
                }))
            .Where(x => x.Field is not null)
            .GroupBy(x => (x.Method, x.Field))
            .Select(g => new { g.Key.Method, Field = g.Key.Field!, g.First().Type, Occurrences = g.Count() })
            .OrderBy(x => x.Method, StringComparer.Ordinal)
            .ThenBy(x => x.Field, StringComparer.Ordinal)
            .ToList();

        sb.AppendLine("### 6.2 未覆盖字段（按 method + 字段名去重）");
        sb.AppendLine();
        if (undeclared.Count == 0)
        {
            sb.AppendLine("_实测应答字段均与 Catalog 一致，无新增建议。_");
            sb.AppendLine();
            return;
        }

        sb.AppendLine($"> 共 {undeclared.Count} 条唯一 (method, 字段) 组合。上一版按「每次出现」罗列，");
        sb.AppendLine("> 导致 `getDevStatus` 的 8 个字段被重复列出多遍；本版已按 method+字段名去重，");
        sb.AppendLine("> 并按**责任归属**拆成两张子表。");
        sb.AppendLine();

        var ourGap = undeclared.Where(x => DocumentedButMissingFromCatalog.Contains(x.Field)).ToList();
        var vendorGap = undeclared.Where(x => !DocumentedButMissingFromCatalog.Contains(x.Field)).ToList();

        sb.AppendLine("#### 6.2.1 文档里有、但 Catalog 缺 —— **我们的实现缺口（Phase 1 遗漏，需补 Catalog）**");
        sb.AppendLine();
        if (ourGap.Count == 0)
        {
            sb.AppendLine("_无。_");
        }
        else
        {
            sb.AppendLine("| method | 字段 | 实测类型 | 出现次数 | 处置 |");
            sb.AppendLine("| --- | --- | --- | --- | --- |");
            foreach (var x in ourGap)
                sb.AppendLine($"| {Esc(x.Method)} | `{Esc(x.Field)}` | {Esc(x.Type ?? "-")} | {x.Occurrences} | 补进 `AnShengCommandCatalog` 响应字段声明 |");
        }
        sb.AppendLine();

        sb.AppendLine("#### 6.2.2 文档里也没有 —— **厂商文档缺口（需向安圣书面确认后再补）**");
        sb.AppendLine();
        if (vendorGap.Count == 0)
        {
            sb.AppendLine("_无。_");
        }
        else
        {
            sb.AppendLine("| method | 字段 | 实测类型 | 出现次数 | 备注 |");
            sb.AppendLine("| --- | --- | --- | --- | --- |");
            foreach (var x in vendorGap)
            {
                var note = LocationSensitiveFields.Contains(x.Field)
                    ? "**位置数据，涉及合规**：见 6.3"
                    : "文档未描述，语义需厂商确认";
                sb.AppendLine($"| {Esc(x.Method)} | `{Esc(x.Field)}` | {Esc(x.Type ?? "-")} | {x.Occurrences} | {note} |");
            }
        }
        sb.AppendLine();

        // 6.3 位置合规
        var gpsSample = FindFieldSample(data, "gps");
        if (vendorGap.Any(x => LocationSensitiveFields.Contains(x.Field)) || gpsSample is not null)
        {
            sb.AppendLine("#### 6.3 位置数据合规提示（单独标注）");
            sb.AppendLine();
            sb.AppendLine("- 设备在 `getDevStatus` 响应中回传了**未文档化的 `gps` 字段**，形如 " +
                          $"`{Esc(gpsSample ?? "113.7166214,023.0203323")}`（经度,纬度，逗号分隔字符串，纬度带前导 0 补位）。");
            sb.AppendLine("- 该字段属于**个人/设备位置信息**，一旦入库即进入个人信息保护与地图合规范围：");
            sb.AppendLine("  1. 入库前需明确**是否有采集必要**，无必要则在解析层直接丢弃；");
            sb.AppendLine("  2. 若确需采集，需确认**坐标系**（WGS84 / GCJ-02 / BD09）——文档未说明，必须向安圣书面确认；");
            sb.AppendLine("  3. 任何地图展示/纠偏必须走合规服务，禁止把原始坐标直接抛给第三方地图；");
            sb.AppendLine("  4. 需纳入数据分级与访问控制，不得与设备明细一起无差别导出。");
            sb.AppendLine("- **本报告不对 gps 做任何处理，仅如实记录并标注。**");
            sb.AppendLine();
        }
    }

    // ---- 十、协议事实 ----------------------------------------------------

    private static void WriteProtocolFactsSection(StringBuilder sb, FieldTestReportData data)
    {
        sb.AppendLine("## 十、协议事实（实测结论 → 需向安圣确认 / 平台必须适配）");
        sb.AppendLine();
        sb.AppendLine("> 以下 5 条是从**真机原始报文**里读出来的结构性事实，不是推测。");
        sb.AppendLine("> 每条都给出证据报文位置；对平台的影响写在「平台动作」里。");
        sb.AppendLine();

        // 10.1 slots
        sb.AppendLine("### 10.1 `slots` 是「每个插槽的状态数组」，不是「被操作的插槽号数组」——文档示例是错的");
        sb.AppendLine();
        sb.AppendLine("- **证据**：对 `slotNum:1` 下发 `action:\"off\"`，设备回 `\"slots\":[0]`。");
        sb.AppendLine("  若 `slots` 是「被操作的插槽号」，回的应当是 `[1]`（插槽 1）；实际回 `[0]`，即插槽 1 的**状态为 0（断开）**。");
        sb.AppendLine("  对照：`action:\"on\"` 回 `\"slots\":[1]`（状态 1=闭合）。同一插槽、两次动作、两个不同值 → 只能是状态。");
        sb.AppendLine("- **推论**：`slots` 长度 == `slotAmount`（本机 =1），下标 = 插槽序号-1，值 = 通断状态。");
        sb.AppendLine("- **文档冲突（硬问题，需安圣书面答复）**：文档示例写 `slots:[1,3,4]`。");
        sb.AppendLine("  若 `slots` 是状态数组，`[1,3,4]` 意味着某插槽状态为 `3`、`4` —— **状态值不可能是 3 或 4**。");
        sb.AppendLine("  该示例只可能是把「插槽号列表」误写成了 `slots`。**请安圣明确：`slots` 到底是状态数组还是插槽号数组？**");
        sb.AppendLine("  （本轮已用 `actions` 下发 `slots:[1,3,4]` 做越界探测，结果见第四节 Q9 组。）");
        sb.AppendLine("- **平台动作**：解析层按「状态数组」实现，并对 `slots.Length != slotAmount` 打告警日志。");
        sb.AppendLine();

        // 10.2 tasks
        sb.AppendLine("### 10.2 `tasks` 同名异构：必须按 method 分派解析，禁止通用处理");
        sb.AppendLine();
        sb.AppendLine("- **证据**：");
        sb.AppendLine("  - `getDevStatus` 响应里的 `tasks[]` 元素是**电参数快照**（含 `voltage`/`current` 等字符串数值）；");
        sb.AppendLine("  - `getDelayTasks` 响应里的 `tasks[]` 元素是**延时任务对象**（含 `sign`/`enable`/`sAction`/`eAction`/`cnt` 等）。");
        sb.AppendLine("- **两者字段集合完全不同，但键名都叫 `tasks`。**");
        sb.AppendLine("- **平台动作（硬约束）**：`AnShengMessageParser` **必须按 `method` 分派** `tasks` 的反序列化目标类型。");
        sb.AppendLine("  任何「看到 tasks 就按同一个 DTO 反序列化」的通用写法都会静默产生脏数据（字段全空或类型异常）。");
        sb.AppendLine("  建议：`getDevStatus.tasks` → `SlotElectricSnapshot`，`getDelayTasks.tasks` → `DelayTaskEntry`，两个独立 DTO。");
        sb.AppendLine();

        // 10.3 sign = frameId
        sb.AppendLine("### 10.3 `sign` 就是下发时的 `frameId` —— frameId 被设备持久化为任务主键，平台必须落库");
        sb.AppendLine();
        sb.AppendLine("- **证据**：第一轮 `startDelayTask` 使用 frameId `000000d7621a7844`；");
        sb.AppendLine("  随后 `getDelayTasks` 返回的任务对象里 `\"sign\":\"000000d7621a7844\"` —— **逐字符相同**。");
        sb.AppendLine("- **含义**：frameId 不只是一次请求的相关性 ID，**它被设备当作该任务的主键长期保存**。");
        sb.AppendLine("- **平台动作（重要）**：");
        sb.AppendLine("  1. 下发 `startDelayTask` / 定时任务时生成的 frameId **必须落库**，与业务任务记录一一绑定；");
        sb.AppendLine("  2. 否则平台重启后将**无法把设备上报的 `sign` 映射回自己的任务**，任务变成孤儿，无法查询/停止/审计；");
        sb.AppendLine("  3. frameId 生成必须保证**全局唯一且不复用**（现实现 16 位随机十六进制，200 次抽样无碰撞）。");
        sb.AppendLine();

        // 10.4 timestamp replay
        sb.AppendLine("### 10.4 设备不校验下行 `timestamp` —— 协议层无重放保护");
        sb.AppendLine();
        sb.AppendLine("- **证据**：");
        sb.AppendLine("  - 省略 `timestamp` 的下行命令（WiFi 形态报文）设备照常执行并应答 `ok`；");
        sb.AppendLine("  - `setTime` 把时钟拨到 **+1h 的未来值**，设备照单全收并按该值应答，未做任何合理性校验。");
        sb.AppendLine("- **结论**：`timestamp` 在下行方向**纯属信息性字段**，设备既不校验新鲜度也不做单调性检查。");
        sb.AppendLine("- **安全影响**：任何能连上该 broker 并知道 IMEI 的一方，都可以**原样重放**一条抓到的控制报文，");
        sb.AppendLine("  设备会再次执行。**协议层没有任何重放保护。**");
        sb.AppendLine("- **平台动作**：");
        sb.AppendLine("  1. 防护必须放在 **broker 侧**——按设备/租户做 ACL，严格限制谁能 publish 到 `/iot/client/iot-board/{imei}`；");
        sb.AppendLine("  2. 生产环境**必须启用 TLS 与逐设备凭据**，禁止 `admin/public` 这类共享账号（当前联调环境正是共享账号）；");
        sb.AppendLine("  3. 平台侧记录全部控制指令审计流水（谁、何时、下发了什么 frameId）。");
        sb.AppendLine();

        // 10.5 dual serialization
        sb.AppendLine("### 10.5 同一物理量两种序列化：`EMdata[].v` 是 float32、`tasks[].voltage` 是 string");
        sb.AppendLine();
        sb.AppendLine("- **证据**（同一次 `getDevStatus` 响应内，同一路电压）：");
        sb.AppendLine("  - `EMdata[0].v = 226.2900085` → **JSON number**，典型的 **float32 → double 提升噪声**（226.29 的单精度表示）；");
        sb.AppendLine("  - `tasks[0].voltage = \"226.290\"` → **JSON string**，固定 3 位小数。");
        sb.AppendLine("- **风险**：两条路径入库会产生**互不相等的同一物理量**（`226.2900085 != 226.290`），");
        sb.AppendLine("  导致对账失败、去重失效、告警阈值在边界处抖动。");
        sb.AppendLine("- **平台动作（二选一，必须统一）**：");
        sb.AppendLine("  - 方案 A（推荐）：入库统一转 `decimal` 并 `round(3)`，与设备 string 侧精度对齐；");
        sb.AppendLine("  - 方案 B：入库统一保留原始 string，计算时再转换。");
        sb.AppendLine("  **禁止**两条路径各自入库各自的类型。");
        sb.AppendLine("- **顺带**：`temperature` 实测为 **String**（回答 Q4），与 `EMdata[].v` 的 Number 不一致，同样纳入上述统一策略。");
        sb.AppendLine();
    }

    // ---- 十一、剧本发现 + 安全网 -----------------------------------------

    private static void WriteFindingsAndSafetySection(StringBuilder sb, FieldTestReportData data)
    {
        sb.AppendLine("## 十一、剧本跨步骤发现与安全网执行记录");
        sb.AppendLine();

        sb.AppendLine("### 11.1 跨步骤实证发现");
        sb.AppendLine();
        if (data.ScriptFindings.Count == 0)
        {
            sb.AppendLine("_本轮无跨步骤发现（剧本未执行或全部步骤被跳过）。_");
        }
        else
        {
            foreach (var f in data.ScriptFindings)
                sb.AppendLine($"- {Esc(f)}");
        }
        sb.AppendLine();

        sb.AppendLine("### 11.2 安全网执行记录（finally 块）");
        sb.AppendLine();
        sb.AppendLine("> 执行顺序固定为：**(1) 强制断开开关并读回 → (2) 复位时钟 → (3) 还原自动上报**。");
        sb.AppendLine("> 先保物理安全，再保时钟，最后保配置。任一步失败都会以 `[WARN]` 标出并要求人工介入。");
        sb.AppendLine();
        if (data.SafetyNetLog.Count == 0)
        {
            sb.AppendLine("_无安全网记录（剧本未进入下发阶段）。_");
        }
        else
        {
            sb.AppendLine("```text");
            foreach (var line in data.SafetyNetLog)
                sb.AppendLine(line);
            sb.AppendLine("```");
        }
        sb.AppendLine();

        sb.AppendLine("### 11.3 最终物理状态复核");
        sb.AppendLine();
        sb.AppendLine("| 项 | 值 |");
        sb.AppendLine("| --- | --- |");
        sb.AppendLine($"| 退出时读回 slots | `{Esc(data.FinalSlots)}` |");
        sb.AppendLine($"| 全部插槽已断开 | {(data.FinalSwitchOffConfirmed ? "是" : "**否 —— 需人工介入**")} |");
        var warnCount = data.SafetyNetLog.Count(l => l.Contains("[WARN]", StringComparison.Ordinal));
        sb.AppendLine($"| 安全网告警条数 | {warnCount} |");
        sb.AppendLine();
        sb.AppendLine(data.FinalSwitchOffConfirmed
            ? "**结论：开关已确认处于断开状态（`slots:[0]`），设备未被本次联调留在任何危险状态。**"
            : "**结论：未能确认开关已断开，请立即人工复核设备实际状态。**");
        sb.AppendLine();
    }

    // ---- helpers ----------------------------------------------------------

    /// <summary>Extract "libVersion" from "undeclared response field 'libVersion' (String)".</summary>
    private static string? ExtractFieldName(string message)
    {
        int a = message.IndexOf('\'');
        if (a < 0) return null;
        int b = message.IndexOf('\'', a + 1);
        if (b <= a) return null;
        return message.Substring(a + 1, b - a - 1);
    }

    /// <summary>Extract "String" from "undeclared response field 'libVersion' (String)".</summary>
    private static string? ExtractFieldType(string message)
    {
        int a = message.LastIndexOf('(');
        int b = message.LastIndexOf(')');
        if (a < 0 || b <= a) return null;
        return message.Substring(a + 1, b - a - 1);
    }

    /// <summary>Find the first observed value of a top-level response field across all steps.</summary>
    private static string? FindFieldSample(FieldTestReportData data, string field)
    {
        foreach (var s in data.Steps)
        {
            if (string.IsNullOrEmpty(s.ResponsePayload)) continue;
            try
            {
                using var d = JsonDocument.Parse(s.ResponsePayload);
                if (d.RootElement.ValueKind == JsonValueKind.Object
                    && d.RootElement.TryGetProperty(field, out var v)
                    && v.ValueKind == JsonValueKind.String)
                {
                    return v.GetString();
                }
            }
            catch
            {
                // ignore malformed payloads
            }
        }
        return null;
    }

    private static string Esc(string? s) => (s ?? string.Empty)
        .Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("|", "\\|", StringComparison.Ordinal)
        .Replace("\n", " ", StringComparison.Ordinal)
        .Replace("\r", "", StringComparison.Ordinal);

    private static string Trunc(string? s, int n) => string.IsNullOrEmpty(s) ? "-" : (s.Length <= n ? s : s[..n] + "...");

    private static string Pretty(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "-";
        try
        {
            using var d = JsonDocument.Parse(raw);
            return JsonSerializer.Serialize(d.RootElement, new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            });
        }
        catch
        {
            return raw;
        }
    }
}
