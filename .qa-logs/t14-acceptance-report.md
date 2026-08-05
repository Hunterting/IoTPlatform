# T14 协议族隔离:充电桩 Legacy 命令归位 — QA 验收报告

验收人:严过关(QA) | 轮次:Round 1(一轮通过) | 路由判定:**NoOne(全部通过)**

## 一、协议层测试

| 范围 | 通过 | 失败 | 说明 |
|---|---|---|---|
| 全量 `IoTPlatform.AnSheng.Tests` | **727** | **0** | 基线 680 + 新增 47,数量吻合 |
| `AnShengProtocolFamilyTests`(新增) | **47** | **0** | 单独 filter 跑,137ms 全绿 |
| 纯协议基线三类<br>(ProtocolTests + ProtocolConformanceTests + LegacyWhitelistTests) | **420** | **0** | 626ms |

**未遇到 DB 播种崩溃 / MySQL 争用**。该测试工程为纯协议工程,不接 DB,全量一次跑通(15s),无需 `--blame` 或分类降级处置。

## 二、3 条验收标准复核

### ① 二开面板不出现 order* — 通过(结构性保证)
`Web/src/app/components/ansheng/CommandConsole.tsx:236`
```ts
export const OPEN_DEVICE_CONSOLE_TEMPLATES: CommandTemplate[] = filterByProtocolFamily(
  OPEN_DEVICE_VISIBLE_METHODS.map(...).filter(...),
  ['OpenProtocol'],          // 只放行二开族
  '二开设备控制台',
);
```
- 非手写数组:由 `COMMAND_TEMPLATE_REGISTRY` 经 `filterByProtocolFamily` 按 `tpl.protocolFamily` **推导**;
- `orderStart/orderEnd/orderUp` 在 registry 中标记为 `ChargingPile`(:119/:130/:141),被 allowSet 剔除,**运行时不可能出现**;
- DEV 模式下被剔除的模板会 `console.warn`,避免静默过滤;
- `AnShengManagementPage.tsx:839` 二开控制台确实传入 `OPEN_DEVICE_CONSOLE_TEMPLATES`,充电桩控制台(:643)传 `CHARGING_PILE_CONSOLE_TEMPLATES`(两族均放行)。

### ② orderStart 报文结构一致 — 通过
`AnShengProtocolFamilyTests.BuildOrderStart_PreservesLegacyWireFormat`(:218)断言齐全:
- 字段顺序 `Assert.Equal(new[]{"method","imei","frameId","timestamp","param"}, topLevelNames)`;
- timestamp:`JsonValueKind.String` + `Assert.Equal(13, tsRaw.Length)` + 与当前时间偏差 <60s;
- 业务参数在 `param` 内(sn/order/limit),且 `Assert.False(root.TryGetProperty("sn"/"order"))` 顶层无泄漏;
- 配套:`WithoutLimit_OmitsLimitField`、`BuildOrderEnd_PreservesLegacyWireFormat`、`BuildCommand_WithoutParams_OmitsParamObject`、`HonorsCallerSuppliedFrameId`(T7-2 在途登记 key 一致性)、`LegacyBuilder_AndCompatibilityShell_ProduceSameShape`(兼容外壳同构)。

### ③ close 遗嘱两族 — 通过
- Legacy 形态:`CloseWill_LegacyShape_IsRecognized`(param 包裹 + 毫秒字符串 ts);
- 二开形态:`CloseWill_OpenProtocolShape_IsRecognized`(平铺 + 秒级 int ts);
- 最简形态:`CloseWill_MinimalPayload_IsRecognized`(仅 imei+method);
- 纯上行:`CloseWill_IsUplinkEventOnly_NotADownlinkCommand` 三重否定断言 —— 不在 LegacyCatalog、不在 OpenCatalog、`Resolver.IsKnown(close)==false`,同时 `Assert.Contains(close, EventMethods)` 确认它是被承认的上行事件。

## 三、核心加固:隐式兜底已消除 — 是

| 检查点 | 结论 | 证据 |
|---|---|---|
| 下发分流改显式判定 | 已改 | `AnShengMqttProtocolAdapter.cs:493` `if (!AnShengProtocolFamilyResolver.TryResolve(method, out var family, out _))` → 直接 `throw NotSupportedException`,零报文出网;`:505` `if (family == ChargingPile)` 才走 Legacy 构造 |
| `AnShengCommandService` | 已改 | `:162` `var protocolFamily = AnShengProtocolFamilyResolver.Resolve(method);` → `:174 AllowLegacyMethod = protocolFamily == ChargingPile`(确认属充电桩族,而非"不在二开目录") |
| `IsLegacyMethod` 残留 | 无风险 | 全仓仅 `AnShengMqttProtocolAdapter.cs:113` 一处,已退化为薄壳 `=> AnShengProtocolFamilyResolver.IsChargingPile(method)`,无独立推断逻辑 |
| `AnShengCommandGuard.cs` 注释 | 已改 | `:102` 给出正确写法 `AllowLegacyMethod = Resolver.Resolve(method) == ChargingPile`;`:154` 明确"T14 起不得再用 `Spec` 字段推断协议族",指出旧写法在"被拒绝"时 Spec 同为 null、两种语义撞值 |
| Builder 入口闸门 | 已加 | `AnShengLegacyCommandBuilder.EnsureChargingPileMethod`(:134)在 `BuildCommand` 首行调用(:111),未登记即 `NotSupportedException` |
| 未知 method 拒绝覆盖 | 9 种全覆盖 | `Resolve_UnknownMethods_AreRejected`(:147)InlineData 9 条:`orderStrat`(拼错)/`OrderStart`(首字母大写)/`orderstart`(全小写)/`ORDERSTART`(全大写)/`orderStart2`(多字符)/`order Start`(空格)/`getSwitchConfig`(历史伪命令)/`rebooot`(二开拼错)/`definitelyNotAProtocolMethod`;另有 `Resolve_NullOrBlank_IsRejected`(null/""/空白)与 `LegacyBuilder_RejectsNonChargingPileMethod`(5 条,含二开 `reboot` 不得走 Legacy 构造) |

全仓 grep 取反推断模式(`!Catalog.Contains` 等)仅命中 6 处**注释文本**(均为"改造前如何/为何不这么做"的说明),**无一处是生效代码逻辑**。

## 四、前端构建

```
vite v6.3.5 build --outDir .t14-qa
✓ 2763 modules transformed. ✓ built in 11.43s
EXIT=0
```
- 无 TS/构建错误;仅存量告警(adapters 重导出命名冲突、单 chunk >500kB),与 T14 无关;
- `.t14-qa` 已清理完毕。

## 五、结论

T14 验收**通过**。4 文件全部落地,3 条验收标准均由测试与结构双重保证,隐式兜底(不在二开目录⇒当 Legacy 真实下发)已从**判据层面**根除,替换为"认识二开 / 认识充电桩 / 不认识即拒绝"的显式三态。无遗留问题(Known Issues:无)。
