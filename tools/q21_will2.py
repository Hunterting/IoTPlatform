"""Q21 遗嘱抓包 v2：监听先起，等用户断电。需手动操作设备断电>30s后上电。"""
import json
import time
import uuid

import paho.mqtt.client as mqtt

HOST, PORT, USER, PWD = "120.79.3.248", 18883, "admin", "public"
IMEI = "863434084755211"
UP = f"/iot/server/iot-board/{IMEI}"
CLIENT_ID = f"q21will_{uuid.uuid4().hex[:6]}"

will_msgs, other_msgs = [], []


def on_connect(c, u, f, rc, props=None):
    print(f"[{time.strftime('%H:%M:%S')}] connect rc={rc}", flush=True)
    # 只订设备上行，避免 # 通配引入噪音（上次 watch_imei 就是 #+/iot/server/# 重叠导致重复计数）
    c.subscribe(UP, qos=1)
    print(f"[{time.strftime('%H:%M:%S')}] 监听 {UP}，等遗嘱 ...", flush=True)
    print("*** 现在请拔掉设备电源，等 30 秒以上再重新上电 ***", flush=True)


def on_message(c, u, msg):
    payload = msg.payload.decode("utf-8", errors="replace")
    stamp = time.strftime("%H:%M:%S")
    try:
        obj = json.loads(payload)
        method = obj.get("method", "?")
        if method == "close":
            will_msgs.append({"ts": stamp, "topic": msg.topic, "raw": payload})
            print(f"\n★★★ 遗嘱 close 报文! [{stamp}] topic={msg.topic}", flush=True)
            print(f"    payload: {payload}", flush=True)
        else:
            other_msgs.append({"ts": stamp, "topic": msg.topic, "method": method, "raw": payload})
            print(f"[{stamp}] {method} :: {payload[:200]}", flush=True)
    except Exception:
        other_msgs.append({"ts": stamp, "topic": msg.topic, "raw": payload})
        print(f"[{stamp}] RAW :: {payload[:200]}", flush=True)


cli = mqtt.Client(client_id=CLIENT_ID, protocol=mqtt.MQTTv311)
cli.username_pw_set(USER, PWD)
cli.on_connect = on_connect
cli.on_message = on_message
cli.connect(HOST, PORT, 30)
cli.loop_start()
time.sleep(1)

# 确认设备在线
now = int(time.time())
cli.publish(f"/iot/client/iot-board/{IMEI}", json.dumps({
    "method": "getDevStatus", "imei": IMEI,
    "frameId": f"prewill{uuid.uuid4().hex[:8]}",
    "timestamp": now
}, separators=(",", ":")), qos=1)
print(f"[{time.strftime('%H:%M:%S')}] 已发探活 getDevStatus", flush=True)

# 等待 180 秒，给足断电+重上电时间
for i in range(180):
    time.sleep(1)
    if i % 30 == 29:
        alive = len(other_msgs) + len(will_msgs)
        print(f"  ... {i+1}s/180s  报文 {alive} 条  will={len(will_msgs)}", flush=True)

cli.loop_stop()
cli.disconnect()

# 重连后确认设备
print(f"\n===== 结果 =====")
print(f"遗嘱(close): {len(will_msgs)} 条")
for w in will_msgs:
    print(f"  topic={w['topic']}")
    print(f"  payload={w['raw']}")

# 分析遗嘱
if will_msgs:
    try:
        obj = json.loads(will_msgs[0]["raw"])
        print(f"\n遗嘱分析:")
        print(f"  method={obj.get('method')}")
        print(f"  imei={obj.get('imei')}")
        print(f"  timestamp 有/无: {'timestamp' in obj} 值={obj.get('timestamp')}")
        print(f"  字段集合: {list(obj.keys())}")
        print(f"  topic 与上行 topic 是否相同: {will_msgs[0]['topic'] == UP}")
    except Exception:
        pass
else:
    print("\n★★★ 仍未抓到遗嘱 —— 请确认:")
    print("  1. 断电 >30s（keepAlive 阈值）")
    print("  2. 断电发生在监听窗口内（180s）")
    print("  3. 如果都在窗口内但仍无遗嘱，那遗嘱 topic 可能不是 /iot/server/iot-board/{imei}")
    print("     —— 这本身也是 Q21 要回答的一半问题")
