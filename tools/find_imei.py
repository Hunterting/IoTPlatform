"""从平台数据库中查找已登记的安圣设备 IMEI（devices.SerialNumber / 待认领池）。"""
import pymysql

conn = pymysql.connect(
    host="192.168.3.7", port=3306, user="root", password="root123",
    database="iot_platform", charset="utf8mb4",
    cursorclass=pymysql.cursors.DictCursor,
)


def show(title, sql):
    print(f"\n===== {title} =====", flush=True)
    try:
        with conn.cursor() as cur:
            cur.execute(sql)
            rows = cur.fetchall()
        if not rows:
            print("  (无记录)", flush=True)
            return
        for r in rows:
            print("  " + " | ".join(f"{k}={v}" for k, v in r.items()), flush=True)
    except Exception as exc:
        print(f"  查询失败: {exc}", flush=True)


with conn.cursor() as cur:
    cur.execute("SHOW TABLES")
    tables = [list(r.values())[0] for r in cur.fetchall()]
cand = [t for t in tables if any(k in t.lower() for k in ("device", "ansheng", "protocol"))]
print("相关表:", ", ".join(cand), flush=True)

show("devices（有 SerialNumber 的）",
     "SELECT Id, Name, SerialNumber, Status, ProtocolConfigId, AppCode "
     "FROM devices WHERE SerialNumber IS NOT NULL AND SerialNumber <> '' LIMIT 30")

show("devices 总数", "SELECT COUNT(*) AS total FROM devices")

for t in tables:
    if "ansheng" in t.lower() or "discover" in t.lower():
        show(f"{t}（全量）", f"SELECT * FROM `{t}` LIMIT 20")

show("protocol_configs",
     "SELECT Id, Name, ProtocolType, IsEnabled FROM protocol_configs LIMIT 20")

conn.close()
