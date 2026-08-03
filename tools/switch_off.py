"""收尾安全：把开关复位为断开，并读回确认。"""
import json
import time
import uuid

import paho.mqtt.client as mqtt

HOST, PORT, USER, PWD = "120.79.3.248", 18883, "admin", "public"
IMEI = "863434084755211"
DOWN = f"/iot/client/iot-board/{IMEI}"
UP = f"/iot/server/iot-board/{IMEI}"

got = []


def on_connect(c, u, f, rc, props=None):
    print(f"[connect] rc={rc}", flush=True)
    c.subscribe(UP, qos=1)


def on_message(c, u, msg):
    payload = msg.payload.decode("utf-8", errors="replace")
    got.append(payload)
    print(f"  <- {payload}", flush=True)


cli = mqtt.Client(client_id=f"safeoff_{uuid.uuid4().hex[:8]}", protocol=mqtt.MQTTv311)
cli.username_pw_set(USER, PWD)
cli.on_connect = on_connect
cli.on_message = on_message
cli.connect(HOST, PORT, 30)
cli.loop_start()
time.sleep(1.5)


def send(obj, label):
    print(f"\n[{label}] -> {json.dumps(obj, separators=(',', ':'))}", flush=True)
    cli.publish(DOWN, json.dumps(obj, separators=(",", ":")), qos=1)
    time.sleep(2.5)


now = int(time.time())
send({"method": "action", "imei": IMEI, "slotNum": 1, "action": "off",
      "frameId": "ffff0001" + uuid.uuid4().hex[:8], "timestamp": now}, "断开开关")
send({"method": "getDevStatus", "imei": IMEI,
      "frameId": "ffff0002" + uuid.uuid4().hex[:8], "timestamp": int(time.time())}, "读回确认")

cli.loop_stop()
cli.disconnect()

print("\n===== 结果 =====", flush=True)
for g in got:
    try:
        o = json.loads(g)
        if o.get("method") == "getDevStatus":
            print(f"  slots = {o.get('slots')}   <-- [0] 表示已断开", flush=True)
            print(f"  设备时钟 timestamp = {o.get('timestamp')}  (服务器 {int(time.time())})", flush=True)
    except Exception:
        pass
