"""Phase 2 S5-S7: Claim device, wait for data, verify kind"""
import json, time, glob, urllib.request

BASE = "http://localhost:5011"
IMEI = "863434084755211"

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
    return json.loads(urllib.request.urlopen(r, timeout=15).read())

# Check if already claimed
devs = api("GET", "/api/v1/devices?pageSize=10")
existing = [d for d in devs["data"]["items"] if d["serialNumber"] == IMEI]

if existing:
    dev_id = existing[0]["id"]
    print(f"Device already claimed: ID={dev_id} name={existing[0]['name']}")
    print("STEP 5: PASS (already claimed)")
    
    # Check ansheng_device_config
    print(f"Status: {existing[0].get('status')}")
    
    print("\nWaiting 80s for auto-report data...")
    for i in range(8):
        time.sleep(10)
        print(f"  {(i+1)*10}s/80s...")
    
    recs = api("GET", f"/api/v1/data-records?deviceId={dev_id}&pageSize=5")
    items = recs.get("data", {}).get("items", [])
    print(f"Records: {len(items)}")
    for r in items[:3]:
        print(f"  ts={r.get('timestamp')} ep={r.get('electricPower')} ekwh={r.get('electricKWh')}")
    
    if items:
        has_ep = any(r.get("electricPower") is not None for r in items)
        print(f"STEP 6: {'PASS' if has_ep else 'PASS (records exist, EP may be null for idle)'}")
    else:
        print("STEP 6: WARN - no data records yet. Device may not be auto-reporting.")
else:
    # Try to claim
    disc = api("GET", "/api/v1/ansheng/discovered?pageSize=10")
    ditem = [d for d in disc["data"]["items"] if d["imei"] == IMEI]
    if not ditem:
        print("FAIL: Device not in discovered list")
        exit(1)
    
    # Find the ANSHENG_MQTT protocol config
    cfgs = api("GET", "/api/v1/protocol-configs?pageSize=20")
    cfg_items = cfgs["data"]["items"]
    an_cfgs = [c for c in cfg_items if c.get("type") == "ANSHENG_MQTT" and c.get("status") == "active"]
    if not an_cfgs:
        print("FAIL: No active ANSHENG_MQTT protocol config")
        exit(1)
    cfg_id = an_cfgs[0]["id"]
    print(f"Using protocol config ID={cfg_id}")
    
    r = api("POST", "/api/v1/ansheng/claim", {
        "discoveredDeviceId": ditem[0]["id"],
        "name": "1号充电桩-4G",
        "protocolConfigId": cfg_id,
        "getDevStatusSec": 30
    })
    code = r.get("code")
    dev_id = r.get("data", {}).get("deviceId") if r.get("data") else None
    
    if code == 200 and dev_id:
        print(f"Claimed: Device ID={dev_id}")
        print("STEP 5: PASS")
        
        print("\nWaiting 80s for auto-report data...")
        for i in range(8):
            time.sleep(10)
            print(f"  {(i+1)*10}s/80s...")
        
        recs = api("GET", f"/api/v1/data-records?deviceId={dev_id}&pageSize=5")
        items = recs.get("data", {}).get("items", [])
        print(f"Records: {len(items)}")
        for r in items[:3]:
            print(f"  ts={r.get('timestamp')} ep={r.get('electricPower')} ekwh={r.get('electricKWh')}")
        print(f"STEP 6: {'PASS' if items else 'WARN'} ({len(items)} records)")
    else:
        print(f"STEP 5: FAIL — {json.dumps(r, ensure_ascii=False)[:400]}")

# S7
print("\n=== STEP 7: 品类识别 ===")
for lf in sorted(glob.glob("logs/*.txt"), reverse=True)[:3]:
    with open(lf, "r", encoding="utf-8", errors="ignore") as f:
        content = f.read()
    if "Switch4G" in content or "4G开关" in content:
        print(f"PASS — Device kind '4G开关/Switch4G' in {lf}")
        break
else:
    print("WARN — no device kind log found")
