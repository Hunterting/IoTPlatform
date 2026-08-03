"""Q21 遗嘱抓包 + 设备重连后状态快照。"""
import json
import time
import uuid

import paho.mqtt.client as mqtt

HOST, PORT, USER, PWD = "120.79.3.248", 18883, "admin", "public"
IMEI = "863434084755211"
DOWN = f"/iot/client/iot-board/{IMEI}"
UP = f"/iot/server/iot-board/{IMEI}"

all_msgs = []


def on_connect(c, u, f, rc, props=None):
    print(f"[connect] rc={rc}  client={c._client_id.decode()}", flush=True)
    c.subscribe("#", qos=1)
    print("[sub] # 全量监听 60s ...", flush=True)


def on_message(c, u, msg):
    payload = msg.payload.decode("utf-8", errors="replace")
    stamp = time.strftime("%H:%M:%S")
    print(f"[{stamp}] {msg.topic} :: {payload[:400]}", flush=True)
    all_msgs.append({"ts": stamp, "topic": msg.topic, "raw": payload})


cli = mqtt.Client(client_id=f"q21_{uuid.uuid4().hex[:6]}", protocol=mqtt.MQTTv311)
cli.username_pw_set(USER, PWD)
cli.on_connect = on_connect
cli.on_message = on_message
cli.connect(HOST, PORT, 30)
cli.loop_start()
time.sleep(2.5)

# 发一条 getDevStatus 看设备是否在线
now = int(time.time())
print(f"\n[探活] getDevStatus ->", flush=True)
cli.publish(DOWN, json.dumps({
    "method": "getDevStatus", "imei": IMEI,
    "frameId": f"q21alive{uuid.uuid4().hex[:8]}",
    "timestamp": now
}, separators=(",", ":")), qos=1)

# 监听 60 秒，看有没有延迟遗嘱或任何主动上报
for i in range(60):
    time.sleep(1)
    if i == 30:
        print(f"  ... 守候 30s/60s，报文 {len(all_msgs)} 条", flush=True)

cli.loop_stop()
cli.disconnect()

# 分析
will_msgs = [m for m in all_msgs if '"close"' in m["raw"] or m["raw"].strip().startswith('{"method":"close"')]
print(f"\n===== 结果 =====")
print(f"总报文: {len(all_msgs)} 条")
print(f"遗嘱(close): {len(will_msgs)} 条")

if will_msgs:
    for w in will_msgs:
        print(f"  ★ 遗嘱报文: topic={w['topic']}")
        print(f"     {w['raw']}")
else:
    print("  ★ 遗嘱报文未捕获到（可能在断电窗口已发出且无 QoS>0 保留）")

# 看设备是否在线
alive = [m for m in all_msgs if "getDevStatus" in m["raw"] and "imei" in m["raw"]]
if alive:
    obj = json.loads(alive[-1]["raw"])
    print(f"\n设备状态: slots={obj.get('slots')}  ts={obj.get('timestamp')}  sig={obj.get('signal')}  net={obj.get('netType')}")
else:
    print("\n设备无应答 —— 可能仍处于离线状态")
