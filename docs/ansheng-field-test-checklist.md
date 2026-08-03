# 安圣 4G 开关真机联调「期望响应」验收清单

> 编制：严过关（QA）｜基准文档：`H:\IoTPlatform\asopen.md`（5320 行）
> 设备：1 台真实 4G 开关（`AnShengDeviceKind.Switch4G`），broker `120.79.3.248:18883`
> 用途：抓包工具抓到的真实报文，**逐字段**对照本清单判定「对/错」，而不是「看起来对」。

---

## 0. 使用说明（重要）

### 0.1 本清单的「必选性」列是推断，不是文档原文

`asopen.md` 的**命令参数表有「必须」列，但应答参数表没有**。因此本清单「必选性」列是我根据语义作出的推断，标注含义：

| 标记 | 含义 |
| --- | --- |
| **恒有** | 推断为每次响应必然出现（如 `method`、`result`） |
| **条件** | 仅特定条件下出现（如 `iccid` 仅 4G 款） |
| **未声明** | 文档应答表未列出，但示例中出现 —— **本身就是待确认项** |

**联调时若实测与「恒有」不符，先怀疑我的推断，再怀疑设备。** 不要直接判定设备错误。

### 0.2 通用字段（asopen.md:106-118）

所有命令与应答共有：

| 字段 | 类型 | 说明 | 行号 |
| --- | --- | --- | --- |
| `method` | string | 命令名称 | 110 |
| `result` | string | `ok`-成功；**`method unsupported`**-设备暂不支持此命令；其他-具体失败原因 | 112 |
| `imei` | string | 设备 imei | 114 |
| `frameId` | string | 响应的 `frameId` 与下发命令的 `frameId` **一样**，用于对应。一般为时间戳字符串（如 `1767078752773`）或递增数值字符串（如 `00001`） | 116 |
| `timestamp` | int | **秒级**时间戳，WiFi 款不支持（4G 开关支持） | 118 |

其他约定：
- 生产环境用**压缩 JSON**（asopen.md:75, 97）
- 一次给一台设备发多个命令，**间隔至少 100ms**，防止命令粘连（asopen.md:169）
- 4G 开关支持全部 5 个能力组 G1–G5

### 0.3 文档示例中的占位数据

大量应答示例把 `imei` 写成 `"1745396239780"`（与 `frameId` 同值），这是**占位符不是真 IMEI**。真实 IMEI 为 15 位数字，文档中仅 `getDevStatus`（864536072949900，行 475）和 `timeEvent`（863434084747622，行 4231）两处是真实抓包。比对时不要拿占位值当基准。

### 0.4 第一轮真机已确认的「字段缺口」类型

本轮（2026-07-31，IMEI `863434084755211`）真机抓包后，确认了一批「文档/实现没对齐」的字段。分两类，后续表格用标签标注：

| 标签 | 含义 | 已知字段 |
| --- | --- | --- |
| **【A·厂商文档缺口】** | `asopen.md` 应答表根本没列，但真机确实回传 | `libVersion`、`model`、`sign`、定位数据（`gps` 虽列但无合规说明） |
| **【B·Phase1 实现缺口】** | 文档有列，但 Phase1 的 `AnShengCommandCatalog`/`Parser` 未声明/未解析 | `version`、`slotAmount`、`phaseAmount`（及 `iccid/signal/slots/tasks/EMdata/netType/temperature` 若 Phase1 未纳入解析） |

> 标签【A】意味着我们与厂商之间存在**隐性契约**，将来若依赖这些字段需先与厂商确认；标签【B】意味着我们的实现比文档弱，是 Phase2 必须补的解析项。

---

## 1. 只读组（第一轮联调，安全无副作用）

### 1.1 `getDevInfo` — 获取设备基本信息（asopen.md:189-287）

应答参数表：行 233-251｜应答示例：行 259-277

| 字段名 | 类型 | 必选性 | 文档原文示例值 | 行号 | 备注 |
| --- | --- | --- | --- | --- | --- |
| `method` | string | 恒有 | `"getDevInfo"` | 237 / 261 | |
| `result` | string | 恒有 | `"ok"` | 239 / 263 | |
| `version` | string | 恒有 | `"SWITCH-EC618X-R24-O-V4.0.8"` | 241 / 265 | ✅ 第一轮实测 = `SWITCH-EC718EPM-O-V4.0.21`（≥4.0.20，故 `getAutoReport`/`q`/`uploadEnable` 应可用，见 Q16）【B·Phase1实现缺口：Catalog未声明】 |
| `slotAmount` | int | 恒有 | `1` | 243 / 267 | ✅ 第一轮实测=1（4G 开关恒有）【B·Phase1实现缺口：Catalog未声明】 |
| `phaseAmount` | int | 恒有 | `1` | 245 / 269 | ✅ 第一轮实测=1【B·Phase1实现缺口：Catalog未声明】 |
| `imei` | string | 恒有 | `"1745396239780"`（占位） | 247 / 271 | 示例值 = frameId，是占位符，见 §0.3 |
| `frameId` | string | 条件 | `"1745396239780"` | 249 / 273 | 同命令 frameId；命令未带则未定义 |
| `timestamp` | int | 恒有 | `1785478927` | 251 / 275 | ✅ 第一轮实测回传真实秒级 int（如 1785478927）；4G 支持 |
| `model` | string | 恒有 | `"Air780EPM"` | — | ✅ 第一轮实测；文档应答表未列【A·厂商文档缺口】→ 见 Q7 |
| `libVersion` | string | 恒有 | `"ansheng-iot-luat-os-lib-V1.3.16"` | — | ✅ 第一轮实测；文档完全未提及【A·厂商文档缺口】→ 见 Q7 |

> 🕳 **坑**：示例中 `imei` 与 `frameId` 同值，无法判断设备真实回显规则。见 **Q19**。

---

### 1.2 `getDevStatus` — 获取设备实时状态（asopen.md:291-579）

应答参数表：行 337-365｜应答示例：行 473-571

**顶层字段**

| 字段名 | 类型 | 必选性 | 文档原文示例值 | 行号 | 备注 |
| --- | --- | --- | --- | --- | --- |
| `method` | string | 恒有 | `"getDevStatus"` | 341 / 479 | |
| `result` | string | 恒有 | `"ok"` | 343 / 485 | |
| `netType` | string | 恒有 | `"4G"` | 345 / 511 | ✅ 第一轮实测="4G" |
| `iccid` | string | 条件 | `"898608481024C0310590"` | 347 / 513 | ✅ 第一轮实测="898608271025D1379707"（4G 款确认有） |
| `signal` | int | 恒有 | `25` | 349 / 483 | ✅ 第一轮实测=22（1-31，>10 正常） |
| `temperature` | **string** | 恒有 | **`"29.0"`（字符串）** | 351 / 567 | ✅ 第一轮实测为字符串 `"29.0"`；表声明 float 错误，按字符串解析 → **Q4 已验证** |
| `gps` | string | 条件 | `"113.2170916,023.4001628"` | 353 / 477 | ✅ 第一轮实测="113.7166214,023.0203323"；设备具备定位能力（见下方合规提醒） |
| `slots` | array\<int\> | 恒有 | `[0]` / `[1]` | 355 / 505-509 | ✅ **length = slotAmount 的 0/1 状态数组**（0-关 1-开），**不是插槽号列表**；实测 `slots.length=1=slotAmount`。文档 `actions` 示例 `[1,3,4]` 非法（3/4 非 0/1）【A·厂商文档示例错误】。`q` 可过滤 |
| `tasks` | array\<object\> | 恒有 | 见下 | 357 / 515-565 | ✅ 实测为「每插槽电量参数」结构（见下方 `tasks[]` 子表）；**与 `getDelayTasks.tasks[]` 异构同名**，Parser 禁止共用类型 |
| `EMdata` | array\<object\> | 恒有 | 见下 | 359 / 489-503 | ✅ 实测电量计数组；数值为 number（见双序列化说明） |
| `imei` | string | 恒有 | `"864536072949900"` | 361 / 475 | ✅ 真实 IMEI |
| `frameId` | string | 条件 | `"1745398603262"` | 363 / 569 | |
| `timestamp` | int | 条件 | `1745398605` | 365 / 481 | |
| `model` | string | **未声明** | `"Air780E"`（文档）/ `"Air780EPM"`（实测） | **487** | ✅ 第一轮实测 `model="Air780EPM"`；应答表未列【A·厂商文档缺口】→ **Q7 已验证** |

> ⚠️ **定位数据合规提醒**：真机 `gps="113.7166214,023.0203323"` 表明设备具备定位能力，`asopen.md` 对该字段仅有格式说明、**无任何隐私/合规约束**。若平台落库或对外暴露 `gps`，需自行补充：采集告知、最小化存储、访问鉴权、脱敏/聚合策略，避免触碰个人信息保护要求。建议把 `gps` 列为「受限字段」并在数据模型层标注。

**`EMdata[]` 子对象**（表：行 455-465｜示例：行 489-503）

| 字段名 | 类型 | 必选性 | 文档原文示例值 | 行号 | 备注 |
| --- | --- | --- | --- | --- | --- |
| `v` | double | 恒有 | `239.0090179` | 459 / 495 | 有效电压 V，**数字，7 位小数** |
| `c` | double | 恒有 | `0.067` | 461 / 493 | 有效电流 A |
| `p` | double | 恒有 | `2.9530001` | 463 / 497 | 有效功率 W |
| `e` | double | 恒有 | `0` | 465 / 499 | 插槽总度数（非订单总度数） |

**`tasks[]` 子对象（getDevStatus —— 「每插槽订单/电量」结构，asopen.md:377-423，共 24 字段）**

> 📌 **idle 状态子集观测（非「重大文档不符」）**：第一轮真机返回 `tasks[0] = {"slotNum":1,"status":"idle","voltage":"226.290","current":"0.032","power":"0.341"}`。
> 这**不是**「结构完全不同」——`status:"idle"` 本身即证明设备采用文档语义模型（`idle`=空闲/结束、`working`=进行中，见行 381）。
> idle 状态只是没有进行中的订单，故订单类字段（`type`/`timeSec`/`maxPower`/`totalKwh`/`closeReason`/`remark` 等）**自然缺席**，不等于文档结构无效。
> **结论：文档的完整字段表依然有效，仅在订单进行中（status=working）才会补齐订单类字段。** 下表保留文档全部 24 个字段，并标注第一轮 idle 实测情况。

| 字段名 | 文档类型 | 必选性 | 第一轮 idle 实测 | 文档原文示例值 | 行号 | 备注 |
| --- | --- | --- | --- | --- | --- | --- |
| `slotNum` | int | 恒有 | ✅ 实测存在(=1) | — | 377 | 插槽编号，从1开始 |
| `type` | string | 条件必选(status=working) | ⏳ idle 时缺席（待订单进行中验证） | `TIME`/`POWER` | 379 | 订单类型 |
| `status` | string | 恒有 | ✅ 实测存在(="idle") | `idle`/`working` | 381 | 订单状态 |
| `timeSec` | int | 条件必选(status=working & type=TIME) | ⏳ idle 时缺席 | — | 383 | 计时秒数 |
| `powerKwh` | double | 条件必选(status=working & type=POWER) | ⏳ idle 时缺席 | — | 385 | 计量电量，度 |
| `powerMaxSec` | int | 条件必选(status=working & type=POWER) | ⏳ idle 时缺席 | `0`不限 | 387 | 计量最大秒数 |
| `maxPower` | int | 条件必选(status=working) | ⏳ idle 时缺席 | `1400`默认 | 389 | 最大功率 W |
| `pullOutStop` | bool | 条件必选(status=working) | ⏳ idle 时缺席 | `true`/`false` | 391 | 拔出自停 |
| `pullOutStopPower` | int | 条件必选(status=working & pullOutStop=true) | ⏳ idle 时缺席 | `3`默认 | 393 | 拔出自停功率 |
| `pullOutStopStartSec` | int | 条件必选(status=working & pullOutStop=true) | ⏳ idle 时缺席 | `0`默认 | 395 | 拔出自停开始判断秒数 |
| `chargeFullStop` | bool | 条件必选(status=working) | ⏳ idle 时缺席 | `true`/`false` | 397 | 充满自停 |
| `chargeFullStopPower` | int | 条件必选(status=working & chargeFullStop=true) | ⏳ idle 时缺席 | `5`默认 | 399 | 充满自停功率 |
| `chageFullStopSec` | int | 条件必选(status=working & chargeFullStop=true) | ⏳ idle 时缺席 | `60`默认 | 401 | 充满自停秒数（⚠️ 文档缺 r 拼写，见 Q6） |
| `chargeFullStopStartSec` | int | 条件必选(status=working & chargeFullStop=true) | ⏳ idle 时缺席 | `0`默认 | 403 | 充满自停开始判断秒数 |
| `remark` | string | 条件必选(status=working) | ⏳ idle 时缺席 | — | 405 | 订单备注/编号 |
| `closeReason` | string | 条件必选(status=working) | ⏳ idle 时缺席 | — | 407 | 关闭原因 |
| `totalSec` | int | 条件必选(status=working) | ⏳ idle 时缺席 | — | 409 | 总运行秒数 |
| `totalKwh` | double | 条件必选(status=working) | ⏳ idle 时缺席 | — | 411 | 总运行度数 |
| `voltage` | double(文档) | 恒有 | ⚠️ 实测存在，但为 **string** `"226.290"`（≠ 文档 double） | — | 413 | 有效电压 V —— **类型冲突见 Q5** |
| `current` | double(文档) | 恒有 | ⚠️ 实测存在，但为 **string** `"0.032"` | — | 415 | 有效电流 A —— **类型冲突见 Q5** |
| `power` | double(文档) | 恒有 | ⚠️ 实测存在，但为 **string** `"0.341"` | — | 417 | 有效功率 W —— **类型冲突见 Q5** |
| `vs` | array | 多相电设备才有 | ⏳ idle 时缺席（单相设备无） | — | 419 | 多相电有效电压数组 |
| `cs` | array | 多相电设备才有 | ⏳ idle 时缺席（单相设备无） | — | 421 | 多相电有效电流数组 |
| `ps` | array | 多相电设备才有 | ⏳ idle 时缺席（单相设备无） | — | 423 | 多相电有效功率数组 |

> 🚨 **异构同名警告（保留，独立问题）**：`getDevStatus.tasks[]`（上表，订单/电量参数）与 `getDelayTasks.tasks[]`（§1.3，延时任务列表，含 `sign/enable/sAction/eAction/secs/cnt`）**同名但结构完全不同**。`EMdata[]`（§1.2 上方，仅 `v/c/p/e` 四字段）是第三套数值结构。三者互不兼容——这是**命名冲突**问题，与「idle 子集」是两件独立的事。Parser **禁止**用同一个 C# 类型跨 method 反序列化，必须分方法定义独立模型。

> ✅ **双序列化已确认 + 类型声明冲突（原 Q5，三方关系）**：同一物理量在报文中确有**三条**序列化路径——
> ① **文档声明**：`voltage`/`current`/`power` 为 **`double`**（asopen.md:413-417）；
> ② **真机 `tasks[]`**：返回 **`string` 且 3 位小数**（`"226.290"`/`"0.032"`/`"0.341"`）；
> ③ **真机 `EMdata[]`**：返回 **`number`（float32→double 噪声，7 位小数）**（`v=226.2900085`/`c=0.0320000`/`p=0.3410000`）。
> **结论**：文档对 `voltage`/`current`/`power` 的 `double` 声明是**文档错误**（§1.2 唯一的真实冲突点），并非结构不符。
> **验收标准**：电量类字段要么**原样存 `tasks[]` 的字符串**，要么取 `EMdata` 数值并 `round(3)` 后存储；**严禁按文档的 `double` 声明去建模/反序列化**，否则 `"226.290"` 当 double 直接解析会丢失精度且与 `EMdata` 对不齐。

**`closeReason` 枚举**（行 431-447）：`CLOSED`(435) / `MANUAL_CLOSED`(437) / `PULL_OUT_STOP_CLOSE`(439) / `CHARGE_FULL_STOP_CLOSE`(441) / `OVER_POWER_CLOSE`(443) / `OVER_TEMPERATURE_CLOSE`(445) / `REACH_MAX_TIME_CLOSE`(447)

**`q` 参数**（行 311）：可选，取值 `slots`,`EMdata`,`tasks` 逗号分隔，**v4.0.20 及以上支持**。

---

### 1.3 `getDelayTasks` — 获取延时任务列表（asopen.md:1917-2041）

应答参数表：行 1961-1975｜子对象表：行 1983-1995｜应答示例：行 2003-2033

| 字段名 | 类型 | 必选性 | 文档原文示例值 | 行号 | 备注 |
| --- | --- | --- | --- | --- | --- |
| `method` | string | 恒有 | `"getDelayTasks"` | 1965 / 2005 | |
| `result` | string | 恒有 | `"ok"` | 1967 / 2007 | |
| `tasks` | array | 恒有 | 见下 | 1969 / 2009-2025 | 「按顺序从插槽 1 到插槽 n」→ 长度应 = slotAmount？**Q14** |
| `imei` | string | 恒有 | `"1745396239780"`（占位） | 1971 / 2027 | |
| `frameId` | string | 条件 | `"1745396239780"` | 1973 / 2029 | |
| `timestamp` | int | 条件 | `1745396759` | 1975 / 2031 | |

**`tasks[]` 子对象（getDelayTasks —— 「延时任务列表」结构，与 getDevStatus.tasks[] 异构同名）**

| 字段名 | 类型 | 必选性 | 实测/文档示例值 | 行号 | 备注 |
| --- | --- | --- | --- | --- | --- |
| `enable` | bool | 恒有 | `true` | 1987 / 2021 | |
| `sAction` | string | 恒有 | **`"none"`** | 1989 / 2017 | 🕳 表只允许 `on`/`off`/`toggle`，**示例却是 `none`** → **Q13**（第一轮未实测 none） |
| `eAction` | string | 恒有 | `"off"` | 1991 / 2015 | `on`/`off`/`toggle`；第一轮实测 `eAction:"off"` |
| `secs` | int | 恒有 | `10` | 1993 / 2019 | 延时秒数；第一轮实测 `secs:10` |
| `cnt` | int | 恒有 | `1` | 1995 / 2013 | **当前计数秒数**（仅出现在响应，命令无此字段）；第一轮实测 `cnt:1` |
| `sign` | string | 恒有 | `"000000d7621a7844"` | — | ✅ **第一轮实测新增**：延时任务的业务主键 = **创建时下发的 `frameId`**。本例 `sign` 与 `startDelayTask` 下发的 `frameId`（`000000d7621a7844`）**完全相同**【A·厂商文档缺口：应答表未列】 |

> ⚠️ **异构同名警告**：此 `tasks[]` 与 `getDevStatus.tasks[]`（§1.2）**同名但结构完全不同**，禁止共用解析类型（见 §1.2 警告）。
> 注意：`tasks[]` 子对象**没有 `slotNum`**，靠数组下标对应插槽。

---

### 1.4 `getAutoReport` — 获取自动上报配置（asopen.md:1007-1119）⚠️ 测试中 + v4.0.20+

应答参数表：行 1051-1077｜应答示例：行 1085-1111

| 字段名 | 类型 | 必选性 | 文档原文示例值 | 行号 | 备注 |
| --- | --- | --- | --- | --- | --- |
| `method` | string | 恒有 | `"getAutoReport"` | 1055 / 1087 | |
| `result` | string | 恒有 | `"ok"` | 1057 / 1089 | 若固件 <4.0.20 预期 `method unsupported` |
| `getDevStatusSec` | int | 恒有 | `600` | 1059 / 1091 | `0`-不上报；非 0 不得 <30 |
| `getDevStatusQ` | string | 恒有 | `"slots,EMdata"` | 1061 / 1093 | |
| `orderUpSec` | int | 恒有 | `0` | 1063 / 1095 | `0`-不上报；非 0 不得 <30 |
| `rs485Sec` | int | 恒有 | `200` | 1065 / 1097 | `0`-不上报；非 0 不得 <30 |
| `rs485BaudRate` | int | 恒有 | `115200` | 1067 / 1099 | 2400~2000000，默认 115200 |
| `rs485SendWaitMs` | int | 恒有 | `300` | 1069 / 1101 | 默认 300 |
| `rs485Array` | array\<string\> | 恒有 | `["3837313131","3a4d558921"]` | 1071 / 1103 | 十六进制命令字符串数组 |
| `imei` | string | 恒有 | `"1745396239780"`（占位） | 1073 / 1105 | |
| `frameId` | string | 条件 | `"1745396239780"` | 1075 / 1107 | |
| `timestamp` | int | 条件 | `1745396759` | 1077 / 1109 | |

> ⚠️ 标题带「（测试中）」+ 正文注明 v4.0.20 及以上（行 1013）。**先跑 getDevInfo 确认 version 再测本命令** → **Q16**

---

### 1.5 `getKeyConfig` — 获取按键配置（asopen.md:715-805）

应答参数表：行 759-773｜应答示例：行 781-797

| 字段名 | 类型 | 必选性 | 文档原文示例值 | 行号 | 备注 |
| --- | --- | --- | --- | --- | --- |
| `method` | string | 恒有 | `"getKeyConfig"` | 763 / 783 | |
| `result` | string | 恒有 | `"ok"` | 765 / 785 | |
| `mode` | int | 恒有 | `1` | 767 / 787 | `0`-无动作；`1`-切换开关；`2`-离线切换开关，联网不动作 |
| `uploadEnable` | bool | 恒有 | `true` | 769 / 789 | 是否上报按键事件 |
| `imei` | string | **未声明** | `"1745396239780"` | **791** | 🕳 **应答表未列 imei，示例有** → **Q18** |
| `frameId` | string | 条件 | `"1745396239780"` | 771 / 793 | |
| `timestamp` | int | 条件 | `1745396759` | 773 / 795 | |

---

## 2. 控制组（第二轮联调，**须用户放行**）

> ⚠️ 这组会真实改变设备状态（通断电、改设备时钟）。**执行前必须经用户确认**，并确保负载侧安全。

### 2.1 `action` — 单插槽开关动作（asopen.md:1705-1805）

应答参数表：行 1761-1775｜应答示例：行 1783-1797
下发示例：`slotNum:1, action:"on", hasStopDelayTask:false`（行 1739-1751）

| 字段名 | 类型 | 必选性 | 文档原文示例值 | 行号 | 备注 |
| --- | --- | --- | --- | --- | --- |
| `method` | string | 恒有 | `"action"` | 1765 / 1785 | |
| `result` | string | 恒有 | `"ok"` | 1767 / 1787 | |
| `slots` | array\<int\> | 恒有 | `[1]`（on）/`[0]`（off） | 1769 / 1789 | ✅ **length=slotAmount 的 0/1 状态数组**；第一轮实测 `action:"on"`→`slots:[1]`、`action:"off"`→`slots:[0]`（动作后状态，与 getDevStatus 一致）。文档示例 `on`→`[0]` 为错误占位 → **Q8 部分验证**（toggle 未测） |
| `imei` | string | 恒有 | `"1745396239780"`（占位） | 1771 / 1791 | |
| `frameId` | string | 条件 | `"1745396239780"` | 1773 / 1793 | |
| `timestamp` | int | 条件 | `1745396759` | 1775 / 1795 | |

### 2.2 `actions` — 多插槽开关动作（asopen.md:1811-1911）

应答参数表：行 1867-1881｜应答示例：行 1889-1903
下发示例：`slotNums:[1,3,4], action:"on"`（行 1845-1857）

| 字段名 | 类型 | 必选性 | 文档原文示例值 | 行号 | 备注 |
| --- | --- | --- | --- | --- | --- |
| `method` | string | 恒有 | `"actions"` | 1871 / 1891 | |
| `result` | string | 恒有 | `"ok"` | 1873 / 1893 | |
| `slots` | array\<int\> | 恒有 | `[1]`（单槽实测） | 1875 / 1895 | ✅ **length=slotAmount 的 0/1 状态数组**；文档示例 `[1,3,4]` **非法**（3/4 非 0/1 状态值），属厂商文档错误【A·厂商文档示例错误】→ **列为向安圣确认的硬问题**；**Q9**（多插槽语义第一轮未测） |
| `imei` | string | 恒有 | `"1745396239780"`（占位） | 1877 / 1897 | |
| `frameId` | string | 条件 | `"1745396239780"` | 1879 / 1899 | |
| `timestamp` | int | 条件 | `1745396759` | 1881 / 1901 | |

> 本机 `slotAmount=1`，第一轮仅下发 `slotNums:[1]`，响应 `slots:[1]`（合法 0/1 状态数组）。文档 `[1,3,4]` 示例已确认非法。**多插槽设备的 `[1,3,4]` 语义仍待证伪** → 见 **Q9**。

### 2.3 `startDelayTask` — 开始延时任务（asopen.md:2047-2149）

应答参数表：行 2109-2121｜应答示例：行 2129-2141

| 字段名 | 类型 | 必选性 | 文档原文示例值 | 行号 | 备注 |
| --- | --- | --- | --- | --- | --- |
| `method` | string | 恒有 | `"startDelayTask"` | 2113 / 2131 | |
| `result` | string | 恒有 | `"ok"` | 2115 / 2133 | |
| `slots` | array\<int\> | 恒有 | `[1]` | — | ✅ **第一轮实测新增**：响应含 `slots`，**length=slotAmount 的 0/1 状态数组**（创建后即时状态）；本例 `startDelayTask(sAction:on)`→`slots:[1]`【A·厂商文档缺口：应答表未列】 |
| `imei` | string | 恒有 | `"1745396239780"`（占位） | 2117 / 2135 | |
| `frameId` | string | 条件 | `"1745396239780"` | 2119 / 2137 | ⚠️ 下发的 `frameId` 会被持久化为该延时任务的 `sign`（业务主键），`getDelayTasks` 回读时以 `sign` 返回 |
| `timestamp` | int | 条件 | `1745396759` | 2121 / 2139 | |

> ⚠️ **协议陷阱**：`startDelayTask(sAction:"on", eAction:"off")` 会**立即闭合开关**（sAction 即时生效），`eAction` 等延时到期才执行。中途 `stopDelayTask` 取消任务 → `eAction` 永不触发 → 开关**永久卡在闭合状态**（见 §6）。
> 🕳 命令参数表把 `enable` 标为**必须**（行 2069），但命令示例（行 2085-2099）**没有 `enable`** → **Q15**（第一轮下发了 enable，未测省略场景）

### 2.4 `stopDelayTask` — 停止延时任务（asopen.md:2155-2243）

应答参数表：行 2203-2215｜应答示例：行 2223-2235

| 字段名 | 类型 | 必选性 | 文档原文示例值 | 行号 | 备注 |
| --- | --- | --- | --- | --- | --- |
| `method` | string | 恒有 | `"stopDelayTask"` | 2207 / 2225 | |
| `result` | string | 恒有 | `"ok"` | 2209 / 2227 | |
| `imei` | string | 恒有 | `"1745396239780"`（占位） | 2211 / 2229 | |
| `frameId` | string | 条件 | `"1745396239780"` | 2213 / 2231 | |
| `timestamp` | int | 条件 | `1745396759` | 2215 / 2233 | |

### 2.5 `setTime` — 设置时间（asopen.md:4925-5013）⚠️ 会改设备时钟

应答参数表：行 4973-4985｜应答示例：行 4993-5005
下发示例：`timestamp:1745456483, frameId:"1745456483900"`（行 4955-4963）

| 字段名 | 类型 | 必选性 | 文档原文示例值 | 行号 | 备注 |
| --- | --- | --- | --- | --- | --- |
| `method` | string | 恒有 | **`"setTime"`** ✅ | 4977 / **4995** | ✅ **第一轮实测 `method="setTime"`**（非 `getSimCheck`）→ 文档示例 4995 行是复制错误，**Q11 已验证** |
| `result` | string | 恒有 | `"ok"` | 4979 / 4997 | |
| `imei` | string | 恒有 | `"863434084755211"` | 4981 / 5001 | ✅ 真实 IMEI（第一轮实测） |
| `frameId` | string | 条件 | `"1745456483900"` | 4983 / 5003 | |
| `timestamp` | int | 恒有 | `1785482527` | 4985 / 4999 | ✅ **第一轮实测回显下发值**（step3 下发 1785482527=真实时间+1h，响应原样回显；step11 复位 1785478931 亦原样回显）→ 设备**不覆盖为自身时钟**，**Q12 已验证** |

> 🔒 **安全提示（时间戳不校验）**：设备对下发的 `timestamp` **无任何时间窗/重放校验**，接受任意值并原样回显。意味着 `setTime` 可被重放或伪造时间戳攻击。平台侧若依赖 `timestamp` 做时序/幂等判断，必须在**服务端**校验，不能信任设备回显值。

---

## 3. 设备事件（6 个上报）

### 3.1 `connected` — 连接 MQTT 成功（asopen.md:585-643）

表：行 611-619｜示例：行 627-635

| 字段名 | 类型 | 必选性 | 文档原文示例值 | 行号 | 备注 |
| --- | --- | --- | --- | --- | --- |
| `method` | string | 恒有 | `"connected"` | 615 / 629 | |
| `imei` | string | 恒有 | `"1745396239780"`（占位） | 617 / 631 | |
| `timestamp` | int | 条件 | `1745396759` | 619 / 633 | |

> **无 `result`，无 `frameId`**（事件无对应命令）。共 3 个字段。

### 3.2 `keyEvent` — 按键事件（asopen.md:649-707）

表：行 675-683｜示例：行 691-699

| 字段名 | 类型 | 必选性 | 文档原文示例值 | 行号 | 备注 |
| --- | --- | --- | --- | --- | --- |
| `method` | string | 恒有 | `"keyEvent"` | 679 / 693 | |
| `imei` | string | 恒有 | `"1745396239780"`（占位） | 681 / 695 | |
| `timestamp` | int | 条件 | `1745396759` | 683 / 697 | |

> **无 `result`，无 `frameId`**，且**不含按键编号/按键类型**——单击才触发（行 655）。
> 受 `getKeyConfig.uploadEnable` 控制：为 `false` 时不上报。

### 3.3 `delayEvent` — 延时任务结束（asopen.md:2251-2325）

表：行 2277-2293｜示例：行 2301-2317

| 字段名 | 类型 | 必选性 | 文档原文示例值 | 行号 | 备注 |
| --- | --- | --- | --- | --- | --- |
| `method` | string | 恒有 | `"delayEvent"` | 2281 / 2303 | |
| `result` | string | 恒有 | `"ok"` | 2283 / 2305 | **事件里少见地带 result** |
| `slotNum` | int | 恒有 | `1` | 2285 / 2307 | 🕳 表格该行是 `\| slotNum \| 是 \| int \| ... \|` —— **4 列挤进 3 列表**，「必须」列串进来了，文档排版错误 |
| `slots` | array\<int\> | 恒有 | `[0]` | 2287 / 2309 | |
| `imei` | string | 恒有 | `"1745396239780"`（占位） | 2289 / 2311 | |
| `frameId` | string | ❓ | `"1745396239780"` | 2291 / 2313 | 🕳 **事件却标「同命令 frameId」**，但事件无对应命令 → **Q20** |
| `timestamp` | int | 条件 | `1745396759` | 2293 / 2315 | |

### 3.4 `timeEvent` — 定时任务触发（asopen.md:4119-4245）

表：行 4145-4161｜`task` 子表：行 4169-4185｜示例：行 4193-4237（**真实抓包**）

| 字段名 | 类型 | 必选性 | 文档原文示例值 | 行号 | 备注 |
| --- | --- | --- | --- | --- | --- |
| `method` | string | 恒有 | `"timeEvent"` | 4149 / 4235 | |
| `taskIndex` | int | 恒有 | `1` | 4151 / 4195 | 任务索引，从 1 开始 |
| `slotNum` | int | 恒有 | `1` | 4153 / 4233 | |
| `slots` | array\<int\> | 恒有 | `[1]` | 4155 / 4225-4229 | |
| `task` | object | 恒有 | 见下 | 4157 / 4199-4223 | 触发的定时任务 |
| `imei` | string | 恒有 | `"863434084747622"` | 4159 / 4231 | ✅ 真实 IMEI |
| `timestamp` | int | 条件 | `1779346021` | 4161 / 4197 | |

> **无 `result`，无 `frameId`**。

**`task` 子对象**

| 字段名 | 类型 | 必选性 | 文档原文示例值 | 行号 | 备注 |
| --- | --- | --- | --- | --- | --- |
| `id` | string | 恒有 | `"1779345917718"` | 4173 / 4207 | 设置定时任务时分配 |
| `enable` | bool | 恒有 | `true` | 4175 / 4203 | |
| `weekDays` | array\<int\> | 恒有 | `[1,4,5]` | 4177 / 4209-4217 | 1-7 = 周一~周日；**空数组=仅一次**，执行后 `enable` 变 `false` |
| `hour` | int | 恒有 | `14` | 4179 / 4221 | |
| `minute` | int | 恒有 | `47` | 4181 / 4201 | |
| `action` | string | 恒有 | `"toggle"` | 4183 / 4219 | `on`/`off`/`toggle` |
| `uploadEnable` | bool | 条件 | `true` | 4185 / 4205 | **v5.0.1 及以上才支持** |

### 3.5 `recv485` — RS485 数据上传（asopen.md:4835-4905）⚠️ 测试中

表：行 4857-4873｜示例：行 4881-4897

| 字段名 | 类型 | 必选性 | 文档原文示例值 | 行号 | 备注 |
| --- | --- | --- | --- | --- | --- |
| `method` | string | 恒有 | `"recv485"` | 4861 / 4883 | |
| `result` | string | 恒有 | `"ok"` | 4863 / 4885 | |
| `data` | string | 恒有 | `"343830303133"` | 4865 / 4887 | 十六进制字符串 |
| `num` | int | 恒有 | `1` | 4867 / 4891 | 对应多命令编号，从 1 开始 |
| `frameId` | string | 条件 | `"1745396239780"` | 4869 / 4893 | 🕳 **「自动上报的此值为空」——「空」是 `""`、`null` 还是字段缺失？** → **Q22** |
| `imei` | string | 恒有 | `"1745396239780"`（占位） | 4871 / 4889 | |
| `timestamp` | int | 条件 | `1745396759` | 4873 / 4895 | |

> 本次联调**大概率无法验证**（需接 RS485 从设备）。

### 3.6 `close` — 遗嘱/离线（**无独立章节**）

`close` 在 `asopen.md` 中**没有 `## ` 段落**，仅作为 MQTT 配置里的 `will` 载荷出现：

| 出处 | 行号 | 原文 |
| --- | --- | --- |
| 配置例子 1 | 27 | `"will":"{\"imei\":\"%imei%\",\"method\": \"close\"}"` |
| 配置例子 2 | 54 | `"will":"{\"imei\":\"%imei%\",\"method\": \"close\"}"` |
| getMqtt 应答示例 | 1421 | `"will": "{\"method\":\"close\",\"imei\":\"%imei%\"}"` |
| setMqtt 命令示例 | 1607 | `"will": "{\"method\":\"close\",\"imei\":\"%imei%\"}"` |

| 字段名 | 类型 | 必选性 | 文档原文示例值 | 行号 | 备注 |
| --- | --- | --- | --- | --- | --- |
| `method` | string | 恒有 | `"close"` | 27 / 1421 | |
| `imei` | string | 恒有 | `%imei%` 替换为真实 IMEI | 27 / 1421 | |

> 🕳 **只有 2 个字段：无 `timestamp`、无 `result`、无 `frameId`**（设备已离线，报文由 broker 代发，内容是**连接时预先登记**的静态串）。
> 🕳 两处写法**字段顺序不同**（27 是 imei 在前，1421 是 method 在前），且 27/54 的 `"method":` 后**有一个空格**。→ **Q21**
> 语义提醒：这是 **MQTT 遗嘱**，仅在**异常离线**（掉电/断网/超 keepAlive）时由 broker 发出；正常 `DISCONNECT` 下线**不会**触发。`keepAlive` 配置为 30s（行 27）。

---

## 4. 联调必须回答的开放问题

> 以下每一条都是**凭 `asopen.md` 无法定论、只能靠真机拍板**的点。这一节是本次联调的真正价值。
> 优先级：**P0** 阻塞（不答就无法继续）｜**P1** 影响解析正确性｜**P2** 影响健壮性/边界

---

**Q1【P0·阻塞】设备响应实际发布到哪个 topic？**
文档给了两种互斥的 `publishTopic` 配置：`/iot/server/iot-board`（行 27，**不含 imei**）与 `/iot/server/iot-board/%imei%`（行 54，**含 imei**）。我们的 `PublishTopicPattern` 是 `/iot/server/iot-board/+`，**只能匹配后者**。若真机烧的是前者，我们一条报文都收不到，且**我们靠 topic 提取 IMEI 的逻辑会整体失效**。
**验证**：抓包工具用 `/iot/server/#` 全量通配订阅，记录任意一条上行报文的**完整 topic 字符串**。这必须是联调第一条被回答的问题。

> ✅ **第一轮结论（已验证）**：上行 topic 恒为 `/iot/server/iot-board/863434084755211`（12/12 匹配 `PublishTopicPattern=/iot/server/iot-board/+`）；下行 topic 为 `/iot/client/iot-board/{imei}`。IMEI 提取逻辑成立。

**Q2【P0】4G 开关是否真的回传 `timestamp`？语义是什么？**
文档称 4G 款支持、WiFi 不支持（行 118/365）。我们的 `SupportsTimestamp()` 据此对 Switch4G 下发时注入 `timestamp`。但**设备是否回传、是否校验下发值**均未说明。
**验证**：(a) `getDevInfo` 响应中是否有 `timestamp`，是否为合理秒级值（10 位）；(b) 下发**不带** `timestamp` 的 `getDevInfo`，看是否仍正常响应——若报错，说明我们必须每条都注入；(c) 对比设备回传值与服务器当前时间的偏移量，判断设备时钟是否准。

> ✅ **第一轮结论（已验证·部分）**：设备**确回传真实秒级 int 时间戳**（如 `getDevInfo.timestamp=1785478927`，10 位合理值）；`setTime` 下发 +1h（step3 ts=1785482527=真实+3600s）后 propagate 正常。**未测**：「不带 timestamp 的 getDevInfo 是否报错」（Q2-b 仍开放）。

**Q3【P0】设备对 16 位十六进制 `frameId` 是否原样回显？**
文档所有示例的 `frameId` 都是**纯数字时间戳串**（如 `1745396239780`，13 位）或递增数字串（行 116）。而我们的 `AnShengCommandBuilder` 生成的是 **16 字符十六进制**（含 a-f 字母）。文档从未出现含字母的 frameId。
**验证**：下发一条 `getDevInfo`，frameId 用我们真实生成的 16 位 hex（如 `a3f81c92d40e7b65`），检查响应中 `frameId` 是否**逐字符相同**——重点看是否被截断（如只回 13 位）、是否被当数字解析导致失真、是否大小写变化。**若不回显一致，我们的命令-响应配对机制整体失效。**

> ✅ **第一轮结论（已验证）**：16 位 hex `frameId` **逐字符原样回显 12/12**，含 a-f（如下发 `000000d0bf3a9586` → 回显 `000000d0bf3a9586`，无截断/无大小写变化）。命令-响应配对机制成立。

**Q4【P1】`temperature` 到底是字符串还是数字？**
应答表声明 `float`（行 351），示例却是字符串 `"32.4"`（行 567）。
**验证**：`getDevStatus` 响应中 `temperature` 的 JSON 字面量——有引号还是没引号。

> ✅ **第一轮结论（已验证）**：`getDevStatus.temperature` 实测为**字符串** `"29.0"`（带引号）。按字符串解析；文档表声明 float 错误（已在 §1.2 表更正）。

**Q5【P1】`tasks[]` 内的数值字段为何被字符串化？是否与 `EMdata` 不一致？**
同一份报文中：`EMdata[0].v = 239.0090179`（数字，7 位小数）而 `tasks[0].voltage = "239.009"`（字符串，3 位小数）；`current`/`power`/`totalKwh` 同理（行 525/529/543/547 vs 493-499）。但多相电 `vs`/`cs`/`ps` 又是数字（行 557-561）。这不像笔误，更像固件里两条独立的序列化路径。
**验证**：需设备**存在订单任务**才能拿到非空 `tasks`——本机是二开开关，可能 `tasks` 恒为空数组。若为空，**本问题本轮无法回答，需明确记为"未验证"**，不可默认按文档表格的 double 实现解析。降级方案：观察 `EMdata` 是否稳定为数字，先锁住这一半结论。

> ✅ **第一轮结论（已验证·双序列化确认 + 类型声明冲突）**：`EMdata[0].v=226.2900085`（number/7dp）vs `tasks[0].voltage="226.290"`（string/3dp）已实测确认。文档对 `voltage`/`current`/`power` 的 `double` 声明是**文档错误**——**严禁按文档的 `double` 声明去建模/反序列化**，应原样存 `tasks[]` 字符串或取 `EMdata` 并 `round(3)`。详见 §1.2 三方关系说明。

**Q6【P1】设备实际发的是 `chageFullStopSec`（错拼）还是 `chargeFullStopSec`（正确）？**
参数表用错拼 `chageFullStopSec`（行 401），应答示例用正确拼写 `chargeFullStopSec`（行 551）。**强证据更新（asopen.md 同表自证）**：行 397/399/403 的 `chargeFullStop*` 均拼写**正确**，唯独 401 写成 `chageFullStopSec`（缺 r）——同一张参数表内自相矛盾，说明该字段名极可能是**由固件字段名直接生成**，固件侧本就拼作 `chageFullStopSec`。
**验证**：仍待「订单进行中」抓包坐实原始 JSON key 字面拼写；若两个 key 同时出现，需单独记录。无论结论如何，**Parser 必须同时兼容两种拼写**（我们已做双字段兼容）。

> 🟡 **第一轮结论（强证据倾向 + 仍需实锤）**：本轮 idle `tasks[]` 未出现该字段，**无法直接判定拼写**；但 `asopen.md` 同表 397/399/403 拼对、唯独 401 缺 r，**强证据倾向：固件拼写就是 `chageFullStopSec`**（参数表由固件字段名生成）。**要求 `AnShengMessageParser` 兼容 `chageFullStopSec` 与 `chargeFullStopSec` 两种 key 长期保留**，待第二轮「订单进行中」抓包最终实锤。

**Q7【P1】`getDevStatus` 响应里还有多少未文档化字段？**
示例中 `"model": "Air780E"`（行 487）**未出现在应答参数表**（行 337-365）。既然存在一个，可能还有更多。
**验证**：把真机 `getDevStatus`/`getDevInfo` 响应的**全部顶层 key 集合**与本清单表格逐一 diff，列出所有「文档没有但设备发了」的字段。这类字段若将来被依赖，会成为隐性契约。

> ✅ **第一轮结论（已验证）**：全量 diff 完成。`getDevInfo` 新增未文档字段 `libVersion="ansheng-iot-luat-os-lib-V1.3.16"`、`model="Air780EPM"`；`getDevStatus` 实测含 `iccid/signal/slots/tasks/EMdata/gps/netType/temperature/model`。厂商文档缺口字段见 §0.4 标签【A】。

**Q8【P1】`action` 响应的 `slots` 是动作前还是动作后的状态？**
示例下发 `slotNum:1, action:"on"`，却返回 `slots:[0]`（关闭，行 1789）。若 `slots` 是动作**后**状态，应为 `[1]`。三种可能：①示例笔误；②`slots` 反映动作**前**状态；③设备异步执行，响应时动作尚未生效。
**验证**：确保插槽初始为关，下发 `action:"on"`，看响应 `slots`；随后立即再下发一条 `getDevStatus` 对比。若 `action` 回 `[0]` 而紧随的 `getDevStatus` 回 `[1]`，即证实②或③。**这直接决定我们能否用 action 响应更新设备状态缓存。**

> ⏳ **第一轮结论（部分验证）**：`action:"on"`→`slots:[1]`、`action:"off"`→`slots:[0]`，**返回的是动作后状态**（与紧随的 `getDevStatus` 一致），可用于更新状态缓存。文档示例 `on`→`[0]` 系错误占位。**仍存疑**：`action:"toggle"` 第一轮未下发，toggle 语义未测。

**Q9【P1】`actions` 响应的 `slots` 语义（示例明显错误）**
示例下发 `slotNums:[1,3,4]`，响应 `slots:[1,3,4]`（行 1895）。但 `slots` 子项只能是 `0`/`1`（行 1875），`3`/`4` 是非法状态值——响应示例显然是把请求的 slotNums 复制过去了。
**验证**：本机 `slotAmount` 预计为 1，无法下发 `[1,3,4]`。降级方案：下发 `slotNums:[1]`，观察响应 `slots` 长度是否 = `slotAmount`、子项是否 ∈{0,1}。**若本机只有 1 个插槽，本问题只能部分回答，多插槽语义需留待多槽设备**——务必记为未验证。

> ⏳ **第一轮结论（未验证）**：第一轮仅下发 `slotNums:[1]`，响应 `slots:[1]`（合法 0/1 状态数组）。文档 `[1,3,4]` 示例已确认非法。**多插槽 `[1,3,4]` 语义仍待多槽设备证伪**。

**Q10【P1】`slots` 数组长度是否恒等于 `slotAmount`？**
多处称「按顺序从插槽 1 到插槽 n」，但从未明确 n 是否 = `getDevInfo.slotAmount`。我们的 slotNum 上界校验缺口（Phase 1 记录的 P2）正依赖这个答案。
**验证**：`getDevInfo.slotAmount` 与 `getDevStatus.slots.length`、`getDelayTasks.tasks.length`、`getDevStatus.EMdata.length` 四者比对。

> ✅ **第一轮结论（已验证）**：`getDevInfo.slotAmount=1`；`getDevStatus.slots.length=1`、`getDevStatus.EMdata.length=1`、`getDelayTasks.tasks.length=1`（有任务时）——**均 = slotAmount**。`slots` 为 length=slotAmount 的 0/1 数组。

**Q11【P1】`setTime` 响应的 `method` 是 `setTime` 还是 `getSimCheck`？**
应答示例（行 4995）赫然写着 `"method": "getSimCheck"`，与表格声明的 `setTime`（行 4977）矛盾。几乎可以肯定是文档复制粘贴错误，但**若固件也是复制粘贴写的，就会真的回错**——那我们的按 method 路由会把响应派发错。
**验证**：下发 `setTime`，看响应 `method` 字面值。

> ✅ **第一轮结论（已验证）**：`setTime` 响应 `method` 实测为 `"setTime"`（非 `getSimCheck`）。文档 4995 行示例为复制错误，按 `setTime` 路由正确。

**Q12【P1】`setTime` 响应的 `timestamp` 是回显下发值还是设备当前时钟？**
示例中下发 `1745456483`（行 4959）与响应 `1745456483`（行 4999）完全相同，两种语义无法区分。
**验证**：下发一个**与真实时间明显偏离**的 timestamp（如当前时间 -3600 秒），观察响应。⚠️ **此操作会真实修改设备时钟**，须在用户放行且知悉的前提下进行，并在测试结束后立即用正确时间戳复位。若用户不同意，本问题记为未验证。

> ✅ **第一轮结论（已验证）**：`setTime` 响应 `timestamp` **原样回显下发值**（step3 下发 1785482527、step11 复位 1785478931，均原样回显），**设备不覆盖为自身时钟**。业务时间戳未被篡改；`setTime` 工作 + 复位正常。

**Q13【P1】延时任务的 `sAction` 能否为 `none`？**
`getDelayTasks` 子表只允许 `on`/`off`/`toggle`（行 1989），但其自身示例是 `"none"`（行 2017）；而 `startDelayTask` 的参数表明确允许 `none`（行 2071）。子表漏写的可能性大，但需确认。
**验证**：`startDelayTask` 下发 `sAction:"none"`，成功后 `getDelayTasks`，看回读的 `sAction`。

> ⏳ **第一轮结论（未验证）**：第一轮 `startDelayTask` 使用 `sAction:"on"`（未用 `none`），`getDelayTasks` 回读 `sAction:"on"`。文档示例 `none` 是否合法**仍未实测**。

**Q14【P2】`getDelayTasks.tasks` 在无任务时是什么？长度是否恒 = slotAmount？**
「按顺序从插槽 1 到插槽 n」（行 1969）暗示定长占位，但未说无任务时是空数组 `[]` 还是含 `enable:false` 的占位对象。
**验证**：在**未设置任何延时任务**的干净状态下调 `getDelayTasks`，记录 `tasks` 的实际形态。

> ⏳ **第一轮结论（部分/未验证）**：第一轮仅在**已创建延时任务**时抓到 `getDelayTasks.tasks`（非空，含 `sign/enable/sAction/eAction/secs/cnt`）。**空任务时的形态（[] 还是占位对象）未知**。

**Q15【P2】`startDelayTask` 的 `enable` 是否真必填？**
参数表标「是」（行 2069），官方命令示例却没带（行 2085-2099）。我们的 Catalog 按必填校验，若设备实际不要求，我们会**比设备更严格**，误拒合法请求。
**验证**：下发一条**不含 `enable`** 的 `startDelayTask`，看 `result` 是否为 `ok`。若 ok，说明我们的必填校验过严，需放宽。

> ⏳ **第一轮结论（未验证）**：第一轮 `startDelayTask` **下发了 `enable`**，`result:ok`。**未测省略 `enable` 的场景**，故无法判定是否真必填；我们的 Catalog 必填校验是否过严仍待验证。

**Q16【P2】本机固件是否支持 `getAutoReport` / `getDevStatus.q` / `uploadEnable`？**
三个版本门槛：`getAutoReport` 与 `q` 需 v4.0.20+（行 1013/311），定时任务 `uploadEnable` 需 v5.0.1+（行 4185）。且 `getAutoReport`/`setAutoReport`/`send485`/`recv485` 标注「测试中」。
**验证**：先 `getDevInfo` 取 `version` 并解析（形如 `SWITCH-EC618X-R24-O-V4.0.8` → 4.0.8），再据此预测各命令可用性，然后实测比对。**注意示例中的 V4.0.8 < 4.0.20，若本机也是老固件，Q17 正好可借它验证。**

> ⏳ **第一轮结论（未验证·但可预测）**：第一轮**未下发** `getAutoReport`/`getDevStatus.q`/`uploadEnable`。但 `getDevInfo.version="SWITCH-EC718EPM-O-V4.0.21"` ≥4.0.20 → **预测这些功能本机支持**，待第二轮实测确认。

**Q17【P2】不支持的命令，`result` 字面量是否精确为 `method unsupported`？**
行 112 规定了这个字符串，但大小写、空格、是否带句点均需实证；我们的解析按此字面量判定"设备不支持"。
**验证**：借 Q16——若本机固件 <4.0.20，直接下发 `getAutoReport` 即可拿到真实的不支持响应。若固件够新，改用一个确定不支持的命令（如向开关下发喇叭专属命令）或一个不存在的 method 名（注意：不存在的 method 可能返回另一种错误，需与"不支持"区分开）。

> ⏳ **第一轮结论（未验证）**：第一轮未触发任何「不支持」响应（`result` 全为 `ok`）。`method unsupported` 字面量仍待实测。

**Q18【P2】`getKeyConfig` 响应是否含 `imei`？**
应答表（行 759-773）未列 `imei`，示例（行 791）却有。属于表格漏写还是示例多写？
**验证**：`getKeyConfig` 响应的顶层 key 集合。此问题可与 Q7 合并为「全命令响应字段全量 diff」一并解决。

> ⏳ **第一轮结论（未验证）**：第一轮**未下发** `getKeyConfig`，响应是否含 `imei` 未知（已并入 Q7 全量 diff 计划，待第二轮）。

**Q19【P2】设备回传的 `imei` 是否为 15 位真实 IMEI，且与 topic 中的 IMEI 一致？**
多数示例把 `imei` 写成 `"1745396239780"`（13 位，与 frameId 同值）——是占位符。我们目前**从 topic 提取 IMEI**，若报文体内的 `imei` 与 topic 段不一致，需明确以哪个为准。
**验证**：比对同一条报文的 topic 末段与 body 内 `imei` 字段。

> ✅ **第一轮结论（已验证）**：报文体内 `imei="863434084755211"` 为 **15 位真实 IMEI**，与 topic 末段 `863434084755211` **完全一致**。以 topic/body 任一处为准均可。

**Q20【P2】`delayEvent` 是否真带 `frameId`？带的是什么？**
表格写「同命令 `frameId`」（行 2291），但 delayEvent 是**自发事件**，没有对应的下发命令。可能是：①带触发它的 `startDelayTask` 的 frameId；②空；③设备自generate。
**验证**：用一个**特征明显的 frameId** 下发 `startDelayTask`（如 `deadbeefdeadbeef`），等延时自然结束，看 `delayEvent` 的 `frameId` 是否等于该值。这条能顺带再验一次 Q3 的 hex 兼容性。

> ⏳ **第一轮结论（未验证）**：第一轮 `stopDelayTask` 在延时到期前取消，未捕获 `delayEvent`，故 `frameId` 来源未知。需第二轮让延时任务自然结束后抓 `delayEvent`。

**Q21【P2】遗嘱 `close` 报文的实际字段、顺序与触发条件**
文档两处写法字段顺序不同（行 27 imei 在前 vs 行 1421 method 在前），且行 27 的 `"method":` 后多一个空格。载荷是设备**连接时登记**的静态串，故实际内容取决于设备固件里烧的模板。
**验证**：拔电或断网触发（**注意需超过 keepAlive=30s**），抓取遗嘱报文原文，确认：①字段集合是否就 2 个；②是否真无 `timestamp`；③发到哪个 topic（与 Q1 联动——若遗嘱 topic 与 publish topic 不同，我们"同 pattern 只订阅一次"的优化就要重审）。

> ⏳ **第一轮结论（阻塞：待用户配合断电 >30s）**：遗嘱 `close` 报文第一轮未捕获（未做断电测试）。**部分已解答**：§8.2 已确认 EMQX 重叠订阅**不去重** → 「同 pattern 只订阅一次」为**必需**而非可选，Phase1 的订阅优化正确。物理断电触发条件（>keepAlive 30s）待用户配合验证。

**Q22【P3】`recv485` 自动上报时 `frameId` 的"空"是何种表示？**
行 4869 称「自动上报的此值为空」——`""`、`null`、还是 key 不存在？三者在反序列化时行为不同。
**验证**：需接 RS485 从设备。**本轮大概率无法验证，建议直接记为未验证**，不要臆测实现。

> ⏳ **第一轮结论（未验证）**：`recv485` 第一轮未捕获（无 RS485 从设备），`frameId` 的「空」表示仍未知。

**Q23【P3】连续下发间隔 100ms 是否足够？**
行 169 建议「每个命令之间最好间隔 100ms，防止命令粘连」。我们的 `AnShengCommandThrottle` 按 100ms 实现，但这是文档的"最好"而非"必须"，真实边界未知。
**验证**：连续下发 5 条 `getDevInfo`，间隔分别取 100ms / 50ms / 20ms，统计响应完整率与 frameId 错配率，确认 100ms 是否留有余量。

> ⏳ **第一轮结论（未验证）**：第一轮命令间隔约 500ms（非 100ms 边界压测），100ms 是否留余量未知。

**Q24【P2】单插槽定时任务命令如何指定插槽？`slotNum` 在文档里系统性缺失**
这不只是 Phase 1 记录的一处笔误，而是**一整组命令的契约缺口**：

| 位置 | 行号 | 是否有 `slotNum` |
| --- | --- | --- |
| `setSlotTimeTasks` 命令参数表 | 3811-3821 | ❌ 无 |
| `setSlotTimeTasks` 命令示例 | 3887 | ✅ **有** `"slotNum": 1` |
| `getSlotTimeTasks` 命令参数表 | 3597-3603 | ❌ 无 |
| `getSlotTimeTasks` 命令示例 | 3611-3617 | ❌ 无 |
| `getSlotTimeTasks` 应答参数表 | 3627-3643 | ❌ 无 |

`getSlotTimeTasks` 的标题是「获取**单个插槽**/开关定时任务」，但**全节没有任何 `slotNum`**——那它靠什么区分是哪个插槽？三种可能：①文档系统性漏写，实际必传；②单插槽设备上省略即默认插槽 1；③这两个命令实际等价于 `getTimeTasks`/`setTimeTasks`（全局版），标题误导。
**验证**（只读、安全，建议第一轮就跑）：
(a) 调用**不带** `slotNum` 的 `getSlotTimeTasks`，看 `result` 是否 `ok`——若 ok，说明 `slotNum` 至少非必传；
(b) 把它的响应与 `getTimeTasks`（全局版，行 2959）的响应**逐字段对比**——若完全相同，即证实可能性③，这两个命令是重复的；
(c) 再调用**带** `slotNum:1` 的 `getSlotTimeTasks`，看是否报错（多传未知字段的容忍度）。
本机 `slotAmount` 预计为 1，**无法区分①和②**——需明确记为部分未验证。

> ⏳ **第一轮结论（未验证）**：第一轮**未下发** `getSlotTimeTasks`/`getTimeTasks`，单插槽定时任务的 `slotNum` 契约缺口仍未实证（文档全节缺失 `slotNum`）。

---

## 5. 联调执行建议

1. **先答 Q1**（topic 形态）——用 `/iot/server/#` 全量订阅。这条不通，后面全部无意义。
2. **第一轮只读**：`getDevInfo` → `getDevStatus` → `getKeyConfig` → `getDelayTasks` → `getAutoReport` → `getSlotTimeTasks`（Q24，只读安全）→ `getTimeTasks`（供 Q24 对比），每条间隔 ≥100ms。`getDevInfo` 必须第一个跑，其 `version` 决定后续哪些命令可测（Q16）。
3. **全量留档**：每条响应的**原始报文字节**（不要经过我们的反序列化）+ topic + 到达时间，存成文件。这是 Phase 2 的回归基线，也是唯一能事后复核的证据。
4. **字段全量 diff**：把实测顶层 key 集合与本清单表格对比，一次性解决 Q7/Q18。
5. **控制组须用户逐条放行**，尤其 `setTime`（改时钟，Q12）和 `action`（真实通断电，注意负载安全）。
6. **诚实标注未验证项**：Q6 依赖订单任务子对象、Q9 依赖多插槽、Q22 依赖 RS485 从设备——本机很可能都不满足。**这些必须白纸黑字记为"未验证"，不能因为"测试通过了"就默认文档正确。**

---

## 6. 协议陷阱 / 运维风险（第一轮真机发现）

### 6.1 🚨 `stopDelayTask` 会导致物理开关永久卡在闭合状态

**现象（第一轮真机实测，step8→step10→step12）**：
1. step8 `startDelayTask(sAction:"on", eAction:"off", secs:10)` → 响应 `slots:[1]`（开关已闭合）；
2. step10 `stopDelayTask` → `result:ok`（任务被取消）；
3. step12 `getDevStatus` 回读 `slots` **仍为 `[1]`**（开关未自动断开）。

**根因**：`startDelayTask` 的语义是——`sAction` **立即生效**（本例 `on` 立刻闭合），`eAction` **等延时到期才执行**（本例 `off` 应在 10s 后断开）。若在中途调用 `stopDelayTask` 取消任务，**`eAction` 永远不会触发**，而 `sAction` 已经把开关拨到了闭合侧 → 开关**永久卡在闭合（通电）状态**，直到有人手动干预或下次下发动作。

**验收要求（写入 Phase2 实现规范）**：
- 任何调用 `stopDelayTask` 的代码路径，**必须显式补发 `eAction`（或等价的物理动作）**，把开关恢复到预期状态；**或**在向用户/运维展示时**明确告警当前物理通断状态**，禁止「取消即完事」的静默处理。
- `AnShengMessageParser`/上层在收到 `stopDelayTask:ok` 后，**不能假设开关已回到 `sAction` 之前的状态**；状态缓存必须以上一次明确的 `action`/`getDevStatus` 为准。
- 建议平台在 UI/日志中对「延时任务被中途取消」做高亮提示，避免用户误以为开关已断开而带电操作。

### 6.2 🔒 时间戳不校验（安全）

见 §2.5 `setTime` 表下安全提示：设备接受任意 `timestamp`、原样回显、无时间窗/重放校验。平台侧时序与幂等判断必须在服务端完成。

### 6.3 ⚠️ 定位数据合规

见 §1.2 `getDevStatus` 表下合规提醒：设备回传 `gps`，文档无合规约束，平台须自行落实采集告知/最小化/鉴权/脱敏。

### 6.4 📋 第二轮已销号（9 项，全部从待验证清单清出）

| Q | 结论 | 证据 |
|---|---|---|
| Q8 (toggle) | toggle 正常翻转，响应 slots 反映翻转后状态 | 第二轮 step 12 toggle→step 13 getDevStatus 验证 |
| Q9 (actions 复数) | 设备拒绝 `slotNums length cannot be greater then 1`（预期失败 PASS） | 第二轮 step 15 |
| Q13 (sAction=none) | 设备拒绝 `action must be on or off or toggle` | 第二轮 step 18 MISMATCH |
| Q15 (省略 enable) | enable 默认值 = **true** | 第二轮 step 20 省略 enable→step 21 回读 enable:true |
| Q16/Q17 (自动上报) | 固件 4.0.21 支持 getAutoReport/setAutoReport，自动上报工作正常 | 第二轮 G6 驻留窗口 4 条自动上报，间隔精确 30s |
| Q20 (delayEvent) | 已捕获。6 字段结构确认，无 frameId，用 sign (=startDelayTask frameId) 标识任务 | 第二轮 step 38 Q20-wait 驻留窗口 #2 |
| Q23 (限流) | **无限流**，10 条 getDevStatus @100ms 全部 PASS，RTT 稳定 489-521ms | 第二轮 steps 27-36 |
| Q24 (slotNum 契约) | getSlotTimeTasks **必须传 slotNum**（不传报 `slotNum is null`），传了正常 | 第二轮 steps 24-25 |

### 6.5 📋 仍在待验证/待确认（5 项）

| Q | 状态 | 阻塞原因 |
|---|---|---|
| Q6 (chageFullStopSec 拼写) | **强证据倾向**：固件拼写即 `chageFullStopSec`，Parser 已双拼写兼容。待订单进行中实锤 | 需设备 status=working（当前 idle，无订单） |
| Q14 (空任务形态) | 待专门验证。第二轮 step 41 getDelayTasks 返回 `cnt:15`（有到期任务），非空状态 | 需在完全无任务时单独调 getDelayTasks |
| Q18 (getKeyConfig imei) | 未验证 | 第二轮未发 getKeyConfig 命令 |
| Q21 (遗嘱断电) | **部分回答**：遗嘱未出现在 `/iot/server/iot-board/{imei}`。topic 位置、结构、触发条件待安圣确认（见 §7.8） | 待安圣答复 |
| Q22 (RS485 auto-report frameId) | 未验证 | 未接 RS485 从设备

---

## 7. 待向安圣确认的问题清单（厂商对接清单）

> 以下 8 条是**必须直接问安圣（厂商）才能定论**的硬问题，散落于各 § 与 Q 中，此处汇总为对接清单，可直接转发厂商。
> 优先级 **【阻断】**= 不定论会写错解析模型；**【重要】**= 影响健壮性/合规。

### 7.1 【阻断】`actions` 文档示例 `slots:[1,3,4]` 与「0/1 状态数组」语义矛盾
- **矛盾点**：`asopen.md` `actions` 示例写 `slots:[1,3,4]`（插槽号列表），但第一轮实测 `action`/`actions` 的 `slots` 是 **length=slotAmount 的 0/1 状态数组**（`action:"on"`→`[1]`、`"off"`→`[0]`，`[1,3,4]` 非 0/1 非法）。
- **需安圣确认**：`slots` 到底是「插槽编号列表」还是「按插槽序的 0/1 状态位掩码」？以哪份为准？我们按**状态位掩码**实现。

### 7.2 【阻断】`setSlotTimeTasks` / `getSlotTimeTasks` 整组缺失 `slotNum`
- **矛盾点**：定时任务组（§ 单插槽定时）的应答/请求参数表**全节无 `slotNum`**，但同文档 `getDevStatus.tasks[]`（行 377）、`getDelayTasks.tasks[]`（均含 slot 关联）都靠 `slotNum`/`sign` 定位插槽。单插槽定时任务如何关联插槽成疑。
- **需安圣确认**：定时任务是「全局单任务」还是「每插槽一个」？若每插槽，`slotNum` 契约是什么？我们暂时**按缺少 `slotNum` 实现并等待澄清**。

### 7.3 【重要】`send485` 的 `sendWaitMs` 语义
- **疑点**：`send485` 下行含 `sendWaitMs`，文档未说明是「下发后等待从设备响应的最大毫秒数」还是「附加延时」。
- **需安圣确认**：`sendWaitMs` 的确切语义与取值范围、超时后是否回 `recv485`、是否阻塞当前会话。

### 7.4 【重要】`model` 字段 ↔ 设备品类映射
- **疑点**：`getDevInfo.model` 实测 `"Air780EPM"`，文档示例 `"Air780E"`；`version` 形如 `SWITCH-EC718EPM-O-V4.0.21`。我们需据此判断功能集（如是否 4G、单相/多相、是否支持 `q` 参数等）。
- **需安圣确认**：`model` 与 `version` 前缀（如 `EC718EPM`/`EC718` 等）到「设备品类/能力矩阵」的**官方映射表**，以便 Catalog 按型号开关命令。

### 7.5 【阻断】`chageFullStopSec` 拼写：固件即此拼法？
- **疑点**：`asopen.md` 同表内 397/399/403 拼 `chargeFullStop*`（正确），唯独 401 为 `chageFullStopSec`（缺 r）。强证据倾向固件字段名就是 `chageFullStopSec`（参数表由固件字段直接生成）。
- **需安圣确认**：订单进行中抓包时，`tasks[]` 里该字段的**字面 key 到底是哪个**？我们已双拼兼容，但希望拿到官方字段名以决定长期保留策略。

### 7.6 【阻断】`tasks[]` 电量字段 `voltage`/`current`/`power` 文档声明 `double`、真机返回 `string`
- **疑点**：文档（行 413-417）声明三字段为 `double`，第一轮真机 `tasks[]` 实测为 **`string` 且 3 位小数**（`"226.290"` 等）；而 `EMdata[]` 同物理量却是 **`number`（float32 噪声，7 位小数）**。这是文档类型声明错误，非结构不符。
- **需安圣确认**：订单进行中 `tasks[]` 的电量字段是否**稳定为 string/3dp**？我们据此**禁止按文档 double 建模**，统一取 `tasks[]` 字符串或 `EMdata` 并 `round(3)`。

### 7.7 【重要】`gps` 字段无任何合规约束
- **疑点**：设备回传 `gps="113.7166214,023.0203323"`，`asopen.md` 仅有格式说明、**无隐私/合规约束**。
- **需安圣确认**：该 `gps` 是「实时定位」还是「安装位置快照」？采集频率与精度？便于平台侧落实采集告知/最小化存储/鉴权/脱敏等合规��求，避免触碰个人信息保护规定。

### 7.8 【阻断】遗嘱 `close` 的 topic 与离线检测链路
- **背景**：`asopen.md` 中 `close` 无独立章节，仅作为 MQTT 配置 `will` 载荷出现（行 27/54/1421/1607），字段仅 `method` 和 `imei` 两个。这是 MQTT 遗嘱，仅在异常离线（掉电/断网/超 keepAlive=30s）时由 broker 代发。
- **实测疑点**：第一轮联调执行断电 >30s 后，**遗嘱 `close` 报文未出现在 `/iot/server/iot-board/{imei}`**，无法确认离线检测链路是否正常工作。可能原因：(a) 遗嘱发到了其他 topic；(b) broker 遗嘱配置与文档不一致；(c) 设备遗嘱模板与预期不同。
- **需安圣确认**：
  1. 遗嘱 `close` 报文到底发到哪个 MQTT topic？是否与设备上行 publish topic（`/iot/server/iot-board/{imei}`）相同？
  2. 若遗嘱 topic 不同，平台订阅策略需要重新评估——目前按 `iot/server/iot-board/+` 单 pattern 订阅，若遗嘱在别的 topic 下则会漏收。
  3. `close` 报文的确切 JSON 结构与字段顺序——文档两处写法不一致（行 27 `imei` 在前 vs 行 1421 `method` 在前）。
  4. 正常 `DISCONNECT` 下线是否会发 `close`，还是仅异常离线才发？
