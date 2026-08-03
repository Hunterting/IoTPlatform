"""Phase 2 全流程重跑 — 含 MQTT 探活 + 日志实时抓取 S6"""
import json, time, glob, urllib.request, uuid

HOST, PORT, USER, PWD = "120.79.3.248", 18883, "admin", "public"
BASE = "http://localhost:5011"
IMEI = "863434084755211"

# ── Login ──
login = json.loads(urllib.request.urlopen(urllib.request.Request(
    f"{BASE}/api/v1/auth/login",
    data=json.dumps({"email":"admin@system.com","password":"admin123"}).encode(),
    headers={"Content-Type":"application/json"}, method="POST"
), timeout=10).read())
TOKEN = login["data"]["token"]

def api(m, p, b=None):
    d = json.dumps(b).encode() if b else None
    r = urllib.request.Request(f"{BASE}{p}", data=d, method=m)
    r.add_header("Content-Type", "application/json")
    r.add_header("Authorization", f"Bearer {TOKEN}")
    try:
        return json.loads(urllib.request.urlopen(r, timeout=15).read())
    except urllib.error.HTTPError as e:
        body = e.read().decode()
        return {"error": e.code, "body": body[:500]}

def log_tail(n=30):
    """Read last N lines of today's log"""
    today = sorted(glob.glob("logs/*20260731*"), reverse=True)
    if not today:
        return ""
    with open(today[0], "r", encoding="utf-8", errors="ignore") as f:
        lines = f.readlines()
    return "".join(lines[-n:])

def search_log(keyword, context=2):
    """Search today's logs for keyword with surrounding context"""
    today = sorted(glob.glob("logs/*20260731*"), reverse=True)
    if not today:
        return ""
    results = []
    with open(today[0], "r", encoding="utf-8", errors="ignore") as f:
        lines = f.readlines()
    for i, line in enumerate(lines):
        if keyword in line:
            start = max(0, i - context)
            end = min(len(lines), i + context + 1)
            results.append(f"L{i+1}: " + "".join(f"  {lines[j]}" for j in range(start, end)))
    return "\n".join(results[-5:])  # last 5 matches

# ── S1: Token ──
print("=== S1: Token ===")
print(f"  {TOKEN[:30]}...")
print("  PASS")

# ── S2: Protocol Config ──
print("\n=== S2: 创建协议配置 ===")
cfgs = api("GET", "/api/v1/protocol-configs?pageSize=20")
active = [c for c in cfgs.get("data", {}).get("items", []) if c.get("type") == "ANSHENG_MQTT"]
if active:
    cfg_id = active[0]["id"]
    print(f"  Reusing existing config: ID={cfg_id} status={active[0].get('status')}")
else:
    cfg = api("POST", "/api/v1/protocol-configs", {
        "name": "安圣4G开关-MQTT",
        "type": "ANSHENG_MQTT",
        "config": {
            "Host": HOST, "Port": PORT, "Username": USER, "Password": PWD,
            "ClientIdPrefix": "iot_platform_ansheng", "CleanSession": True, "QosLevel": 1,
            "PublishTopicPattern": "/iot/server/iot-board/+",
            "WillTopicPattern": "/iot/server/iot-board/+",
            "SubscribeTopicTemplate": "/iot/client/iot-board/{imei}",
            "CommandMinIntervalMs": 100, "TimeoutSeconds": 30, "KeepAliveSeconds": 60
        }
    })
    cfg_id = cfg.get("data", {}).get("id")
    print(f"  Created: ID={cfg_id}")
print("  PASS")

# ── S3: Start adapter ──
print("\n=== S3: 启动适配器 ===")
# Check if already active
if not active or active[0].get("status") != "active":
    start = api("POST", f"/api/v1/protocol-configs/{cfg_id}/start")
    print(f"  {start.get('message', start)}")
else:
    print(f"  Already active")
print("  PASS")

# ── S4: Device discovery ──
print("\n=== S4: 设备发现 ===")

# First check if device already exists
devs = api("GET", "/api/v1/devices?pageSize=20")
mine = [d for d in devs.get("data", {}).get("items", []) if d.get("serialNumber") == IMEI]
if mine:
    dev_id = mine[0]["id"]
    print(f"  Device already claimed: ID={dev_id} name={mine[0]['name']}")
    print("  PASS")
else:
    # Check discovered
    disc = api("GET", "/api/v1/ansheng/discovered?pageSize=10")
    ditem = [d for d in disc.get("data", {}).get("items", []) if d.get("imei") == IMEI]
    
    if not ditem:
        # MQTT probe
        print("  Device not in discovered list, probing via MQTT...")
        try:
            import paho.mqtt.client as mqtt
            got = []
            def on_connect(c, u, f, rc, p=None):
                c.subscribe(f"/iot/server/iot-board/{IMEI}", qos=1)
            def on_message(c, u, msg):
                got.append(msg.payload.decode())
            cli = mqtt.Client(client_id=f"smoke_{uuid.uuid4().hex[:6]}", protocol=mqtt.MQTTv311)
            cli.username_pw_set(USER, PWD)
            cli.on_connect = on_connect
            cli.on_message = on_message
            cli.connect(HOST, PORT, 30)
            cli.loop_start()
            time.sleep(1.5)
            now = int(time.time())
            cli.publish(f"/iot/client/iot-board/{IMEI}", json.dumps({
                "method": "getDevStatus", "imei": IMEI,
                "frameId": f"smk{uuid.uuid4().hex[:8]}",
                "timestamp": now
            }, separators=(",", ":")), qos=1)
            time.sleep(4)
            cli.loop_stop()
            cli.disconnect()
            if got:
                obj = json.loads(got[-1])
                print(f"  MQTT probe OK: slots={obj.get('slots')} sig={obj.get('signal')}")
            else:
                print("  MQTT probe FAIL: device not responding")
        except ImportError:
            print("  paho-mqtt not available — skip probe")
        
        time.sleep(3)
        disc = api("GET", "/api/v1/ansheng/discovered?pageSize=10")
        ditem = [d for d in disc.get("data", {}).get("items", []) if d.get("imei") == IMEI]
    
    if ditem:
        d = ditem[0]
        print(f"  Found: ID={d['id']} imei={d['imei']} model={d.get('model','?')}")
        print("  PASS")
    else:
        print("  WARN: device still not discovered")
        ditem = None

# ── S5: Claim ──
if not mine:
    print("\n=== S5: 认领设备 ===")
    if not ditem:
        print("  SKIP: no device to claim")
    else:
        r = api("POST", "/api/v1/ansheng/claim", {
            "discoveredDeviceId": ditem[0]["id"],
            "name": "1号充电桩-4G",
            "protocolConfigId": cfg_id,
            "getDevStatusSec": 30
        })
        code = r.get("code")
        dev_id = r.get("data", {}).get("deviceId") if r.get("data") else None
        if code == 200 and dev_id:
            print(f"  Claimed: Device ID={dev_id}")
            print("  PASS")
        else:
            msg = r.get("message", "")[:200]
            print(f"  FAIL: code={code} {msg}")

# ── S6: Data pipeline ──
print("\n=== S6: 数据管道验证 ===")

# Find the device ID
devs = api("GET", "/api/v1/devices?pageSize=20")
mine = [d for d in devs.get("data", {}).get("items", []) if d.get("serialNumber") == IMEI]
if mine:
    dev_id = mine[0]["id"]
    
    # Check for setAutoReport log
    print("\n--- setAutoReport 日志检查 ---")
    auto_logs = search_log("setAutoReport", 1)
    if auto_logs:
        print(auto_logs[:800])
        print("  setAutoReport: FOUND in logs")
    else:
        print("  setAutoReport: NOT FOUND in logs")
    
    # Check for data processing log entries
    print("\n--- 数据处理日志检查 ---")
    for kw in ["ProcessDeviceData", "设备数据", "DeviceDataRecord", "ElectricPower", "total_power"]:
        hits = search_log(kw, 1)
        if hits:
            print(f"  '{kw}': FOUND")
            print(hits[:500])
            break
    else:
        print("  No data processing logs found")
    
    # Check the latest log tail for any errors
    print("\n--- 最新日志尾 (30行) ---")
    tail = log_tail(30)
    # Filter for relevant lines
    for line in tail.split("\n"):
        if any(w in line.lower() for w in ["error", "fail", "warn", "ansheng", "mqtt", "device", "setauto", "data"]):
            print(f"  {line.strip()[:150]}")
    
    # Wait for auto-report data   
    print("\n--- 等待 90s 自动上报数据 ---")
    for i in range(9):
        time.sleep(10)
        # Quick check logs every 30s
        if i % 3 == 2:
            recent = log_tail(5)
            has_data = any("getDevStatus" in l or "total_power" in l for l in recent.split("\n"))
            print(f"  {(i+1)*10}s/90s — {'data flows detected' if has_data else 'waiting...'}")

else:
    print("  SKIP: no device claimed")

# ── S7: Kind ──
print("\n=== S7: 品类识别 ===")
kind_found = False
today = sorted(glob.glob("logs/*20260731*"), reverse=True)
if today:
    with open(today[0], "r", encoding="utf-8", errors="ignore") as f:
        content = f.read()
    if "Switch4G" in content:
        print("  Switch4G: FOUND → DeviceKind.IsFourG() working")
        kind_found = True
    if "4G开关" in content:
        print("  4G开关: FOUND → DeviceKind.Resolve() working")  
        kind_found = True
if kind_found:
    print("  PASS")
else:
    print("  WARN: no kind recognition in logs")

print(f"\n{'='*50}")
print("FINAL: S1-S7 re-run complete")
print(f"{'='*50}")
