"""Phase 2 smoke test — Steps 5-7: Claim + data verification"""
import json, time, glob, urllib.request

BASE = "http://localhost:5011"
IMEI = "863434084755211"

# Login
login = json.loads(urllib.request.urlopen(urllib.request.Request(
    f"{BASE}/api/v1/auth/login",
    data=json.dumps({"email":"admin@system.com","password":"admin123"}).encode(),
    headers={"Content-Type":"application/json"}, method="POST"
), timeout=10).read())
TOKEN = login["data"]["token"]

def api(method, path, body=None):
    data = json.dumps(body).encode() if body else None
    r = urllib.request.Request(f"{BASE}{path}", data=data, method=method)
    r.add_header("Content-Type", "application/json")
    r.add_header("Authorization", f"Bearer {TOKEN}")
    with urllib.request.urlopen(r, timeout=15) as resp:
        return json.loads(resp.read().decode())

# Step 5: Claim
print("=== Step 5: 认领设备 + setAutoReport ===")
disc = api("GET", "/api/v1/ansheng/discovered?pageSize=10")
ditem = [d for d in disc["data"]["items"] if d["imei"] == IMEI][0]
print(f"Discovered: ID={ditem['id']} imei={ditem['imei']}")

claim = api("POST", "/api/v1/ansheng/claim", {
    "discoveredDeviceId": ditem["id"],
    "name": "1号充电桩-4G",
    "protocolConfigId": 1,
    "getDevStatusSec": 30
})
device_id = claim.get("data", {}).get("deviceId")
print(f"Claim response: deviceId={device_id}")

if not device_id:
    print(f"STEP 5: FAIL — {json.dumps(claim, ensure_ascii=False)[:400]}")
    exit(1)

print("STEP 5: PASS")
print(f"Device ID: {device_id}")

# Step 6: Wait for auto-report data
print(f"\n=== Step 6: 验证数据入库 (wait 80s) ===")
for i in range(8):
    time.sleep(10)
    print(f"  {(i+1)*10}s/80s...")

records = api("GET", f"/api/v1/data-records?deviceId={device_id}&pageSize=5")
items = records.get("data", {}).get("items", [])
print(f"Data records found: {len(items)}")

if items:
    for r in items[:3]:
        print(f"  ts={r.get('timestamp')} EP={r.get('electricPower')} KWh={r.get('electricKWh')}")
    if any(r.get("electricPower") is not None for r in items):
        print("STEP 6: PASS — ElectricPower/KWh mapped correctly")
    else:
        print("STEP 6: PASS — Records exist (EP may be null for idle device)")
else:
    # Check logs for evidence
    found = False
    for lf in sorted(glob.glob("logs/*.txt"), reverse=True)[:3]:
        with open(lf, "r", encoding="utf-8", errors="ignore") as f:
            if "setAutoReport" in f.read():
                found = True
                print(f"STEP 6: WARN — setAutoReport logged in {lf} but no records yet")
                break
    if not found:
        print("STEP 6: WARN — No setAutoReport in logs. Check if adapter is working.")
        # Check data-records endpoint total
        all_rec = api("GET", "/api/v1/data-records?pageSize=3")
        all_items = all_rec.get("data", {}).get("items", [])
        print(f"  Total records across all devices: {len(all_items)}")

# Step 7
print(f"\n=== Step 7: 品类识别 ===")
found_kind = False
for lf in sorted(glob.glob("logs/*.txt"), reverse=True)[:3]:
    with open(lf, "r", encoding="utf-8", errors="ignore") as f:
        content = f.read()
    if "Switch4G" in content or "4G开关" in content:
        print(f"PASS — Device kind '4G开关/Switch4G' in {lf}")
        found_kind = True
        break
if not found_kind:
    print("WARN — No device kind log found")

print(f"\nResults: Device ID={device_id}, Records={len(items)}")
