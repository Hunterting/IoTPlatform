"""对指定 IMEI 发一条 getDevInfo 探针，判定设备是否在线应答。"""
import json
import sys
import time

import paho.mqtt.client as mqtt

HOST, PORT, USER, PWD = "120.79.3.248", 18883, "admin", "public"
IMEI = sys.argv[1] if len(sys.argv) > 1 else "863434084755211"
WAIT = int(sys.argv[2]) if len(sys.argv) > 2 else 25

DOWN = f"/iot/client/iot-board/{IMEI}"
UP = f"/iot/server/iot-board/{IMEI}"
PROBE_FRAME = "a3f81c92d40e7b65"

got = []


def on_connect(c, u, f, rc, props=None):
    print(f"[connect] rc={rc}", flush=True)
    c.subscribe("/iot/server/#", qos=1)
    print(f"[sub] /iot/server/#  (期望应答落在 {UP})", flush=True)


def on_message(c, u, msg):
    payload = msg.payload.decode("utf-8", errors="replace")
    got.append((msg.topic, payload))
    print(f"\n>>> 收到上行  topic={msg.topic}", flush=True)
    print(f"    原始载荷: {payload}", flush=True)
    try:
        obj = json.loads(payload)
        fid = obj.get("frameId")
        print(f"    method={obj.get('method')}  imei={obj.get('imei')}  result={obj.get('result')}", flush=True)
        print(f"    frameId={fid!r}", flush=True)
        if fid is not None:
            same = str(fid) == PROBE_FRAME
            print(f"    ★ frameId 回显比对: {'原样回显 (Q3 通过)' if same else f'★被改写! 下发={PROBE_FRAME} 回传={fid}'}", flush=True)
    except Exception as exc:
        print(f"    (JSON 解析失败: {exc})", flush=True)


cli = mqtt.Client(client_id=f"probe_{int(time.time()) % 100000}", protocol=mqtt.MQTTv311)
cli.username_pw_set(USER, PWD)
cli.on_connect = on_connect
cli.on_message = on_message
cli.connect(HOST, PORT, 30)
cli.loop_start()
time.sleep(2)

# 探针一：不带 timestamp（模拟平台冷启动、品类 Unknown 的真实情形）
p1 = json.dumps({"imei": IMEI, "method": "getDevInfo", "frameId": PROBE_FRAME}, separators=(",", ":"))
print(f"\n[TX-1] {DOWN}\n       {p1}", flush=True)
print("       (无 timestamp — 模拟冷启动 品类=Unknown)", flush=True)
cli.publish(DOWN, p1, qos=1)
time.sleep(WAIT // 2)

if not got:
    # 探针二：带秒级 timestamp（品类已知为 4G 的情形）
    p2 = json.dumps({"imei": IMEI, "method": "getDevInfo", "frameId": "b7e2049fa1c8d356",
                     "timestamp": int(time.time())}, separators=(",", ":"))
    print(f"\n[TX-2] {DOWN}\n       {p2}", flush=True)
    print("       (带秒级 timestamp — 模拟品类已识别为 4G)", flush=True)
    cli.publish(DOWN, p2, qos=1)
    time.sleep(WAIT // 2)

cli.loop_stop()
cli.disconnect()

print("\n===== 探针结论 =====", flush=True)
if got:
    print(f"设备在线并应答，共 {len(got)} 条上行。", flush=True)
else:
    print("无任何应答 —— 设备未连接 broker，或未订阅下行 topic。", flush=True)
