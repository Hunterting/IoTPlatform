"""遗嘱未出现在上行topic，换全量#订阅确认是否落到其他topic，以及是否有retained遗嘱。"""
import json
import time
import uuid

import paho.mqtt.client as mqtt

HOST, PORT, USER, PWD = "120.79.3.248", 18883, "admin", "public"
IMEI = "863434084755211"

all_msgs = []


def on_connect(c, u, f, rc, props=None):
    print(f"[{time.strftime('%H:%M:%S')}] connect rc={rc}", flush=True)
    c.subscribe("#", qos=1)
    print("[sub] # 全量监听 30s（含 retained）...", flush=True)


def on_message(c, u, msg):
    payload = msg.payload.decode("utf-8", errors="replace")
    stamp = time.strftime("%H:%M:%S")
    try:
        obj = json.loads(payload)
        method = obj.get("method", "?")
        tag = "★ WILL" if method == "close" else method
    except Exception:
        tag = "RAW"
    print(f"[{stamp}] {msg.topic} [{tag}] :: {payload[:300]}", flush=True)
    all_msgs.append({"ts": stamp, "topic": msg.topic, "raw": payload})


cli = mqtt.Client(client_id=f"willcheck_{uuid.uuid4().hex[:6]}", protocol=mqtt.MQTTv311)
cli.username_pw_set(USER, PWD)
cli.on_connect = on_connect
cli.on_message = on_message
cli.connect(HOST, PORT, 30)
cli.loop_start()

# 也给设备发个探活
time.sleep(2)
now = int(time.time())
cli.publish(f"/iot/client/iot-board/{IMEI}", json.dumps({
    "method": "getDevStatus", "imei": IMEI,
    "frameId": f"check{uuid.uuid4().hex[:8]}",
    "timestamp": now
}, separators=(",", ":")), qos=1)
print(f"[{time.strftime('%H:%M:%S')}] 探活已发", flush=True)

for i in range(30):
    time.sleep(1)

cli.loop_stop()
cli.disconnect()

close_msgs = [m for m in all_msgs if '"method":"close"' in m["raw"] or m["raw"].strip().startswith('{"imei"') and '"close"' in m["raw"]]
topics = set(m["topic"] for m in all_msgs)

print(f"\n===== 结果 =====")
print(f"总报文: {len(all_msgs)} 条")
print(f"遗嘱(close): {len(close_msgs)} 条")
print(f"出现过的 topic: {sorted(topics)}")
if close_msgs:
    for c in close_msgs:
        print(f"  ★ WILL topic={c['topic']} raw={c['raw'][:200]}")
else:
    print("\n★★ 确认：遗嘱未出现在任何 topic 上（含 retained）")
    print("    可能原因: ① 遗嘱 topic 不是 /iot/server/iot-board/{imei}")
    print("             ② 设备 CONNECT 时未设置遗嘱（Will Flag=0）")
    print("             ③ broker 配置了 Will Delay 导致发布晚于监听窗口")
