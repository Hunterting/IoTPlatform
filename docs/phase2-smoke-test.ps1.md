# Phase 2 端到端冒烟测试—执行脚本

## 前提
- API 已启动：`dotnet run`（端口 http://localhost:5011）
- MySQL + Redis 已运行
- 设备 IMEI: 863434084755211，已上电且 MQTT 已连接 broker 120.79.3.248:18883

## Step 1: 获取 JWT Token

```powershell
$body = @{ username = "admin"; password = "admin123" } | ConvertTo-Json
$token = (Invoke-RestMethod -Uri "http://localhost:5011/api/v1/auth/login" -Method POST -Body $body -ContentType "application/json").token
$headers = @{ Authorization = "Bearer $token"; "Content-Type" = "application/json" }
```

## Step 2: 创建协议配置

```powershell
$configBody = @{
    name = "安圣4G开关-MQTT"
    type = "ANSHENG_MQTT"
    config = @{
        Host = "120.79.3.248"
        Port = 18883
        Username = "admin"
        Password = "public"
        ClientIdPrefix = "iot_platform_ansheng"
        CleanSession = $true
        QosLevel = 1
        PublishTopicPattern = "/iot/server/iot-board/+"
        WillTopicPattern = "/iot/server/iot-board/+"
        SubscribeTopicTemplate = "/iot/client/iot-board/{imei}"
        CommandMinIntervalMs = 100
        TimeoutSeconds = 30
        KeepAliveSeconds = 60
    }
} | ConvertTo-Json -Depth 4

$cfg = Invoke-RestMethod -Uri "http://localhost:5011/api/v1/protocol-configs" -Method POST -Body $configBody -Headers $headers
$cfgId = $cfg.data.id
Write-Host "协议配置 ID: $cfgId"
```

**验证**: 返回 200，`id > 0`。

## Step 3: 启动协议适配器

```powershell
Invoke-RestMethod -Uri "http://localhost:5011/api/v1/protocol-configs/$cfgId/start" -Method POST -Headers $headers
```

**验证**: 日志显示 "安圣 MQTT 协议适配器连接成功"、"已订阅安圣数据主题 /iot/server/iot-board/+"

## Step 4: 等待设备自动发现

等 10-15 秒让设备上行一次（发条 getDevStatus 给它，或者等设备自然心跳）。

```powershell
Start-Sleep -Seconds 15
$discovered = Invoke-RestMethod -Uri "http://localhost:5011/api/v1/ansheng/discovered?pageSize=10" -Method GET -Headers $headers
$discovered.data.items | Format-Table id, imei, model, netType, isClaimed
```

**验证**: 列表包含 IMEI=863434084755211，IsClaimed=false。

## Step 5: 认领设备（带自动上报）

```powershell
$devId = ($discovered.data.items | Where-Object { $_.imei -eq "863434084755211" }).id
$claimBody = @{
    discoveredDeviceId = $devId
    name = "1号充电桩-4G"
    protocolConfigId = $cfgId
    getDevStatusSec = 30
} | ConvertTo-Json

$claim = Invoke-RestMethod -Uri "http://localhost:5011/api/v1/ansheng/claim" -Method POST -Body $claimBody -Headers $headers
Write-Host "设备 ID: $($claim.data.deviceId)"
```

**验证**: 返回 200，DeviceId > 0。日志: "安圣命令已发送: Method=setAutoReport"。

## Step 6: 验证自动上报数据入库

等 60 秒（设备 2 个上报周期）。

```powershell
Start-Sleep -Seconds 60
$records = Invoke-RestMethod -Uri "http://localhost:5011/api/v1/data-records?deviceId=$($claim.data.deviceId)&pageSize=5" -Method GET -Headers $headers
$records.data.items | Format-Table id, timestamp, electricPower, electricKWh
```

**验证**: 至少 1 条记录，`electricPower`/`electricKWh` 已正确映射。

## Step 7: 验证品类识别

```powershell
# 检查日志输出
# 应包含: "识别安圣设备品类: IMEI=863434084755211, Kind=4G开关"
Get-Content -Path "logs/*.log" -Tail 50 | Select-String "Kind=4G" | Select-Object -First 1
```

**验证**: 日志无 "未知品类" 字样。

## 快速验证（手动版本，适合无完整环境的场景）

如果 API 不可用，可以用 Python 脚本旁路验证 MQTT 链路：

```python
# tools/q21_check.py 已就绪
# 发 getDevStatus → 设备应答 → 确认 MQTT 链路通
# 设备品类识别可用 FieldTest 工具验证（--kind Switch4G）
```

---

## T02 完成标准

| # | 检查项 | 通过标准 |
|---|---|---|
| 1 | 协议配置创建 | API 返回 id > 0 |
| 2 | 适配器启动 | 日志显示 MQTT 连接成功 |
| 3 | 设备自动发现 | discovered 列表含 IMEI 863434084755211 |
| 4 | 认领 + setAutoReport | DeviceId > 0，日志显示命令已发送 |
| 5 | 数据入库 | device_data_records 有记录 |
| 6 | 品类识别 | 日志确认 Kind=4G开关（非 Unknown） |
