"""长时守候：全量订阅，捕获任意设备上行并从 topic 提取 IMEI；高亮遗嘱 close 报文。"""
import json
import re
import sys
import time

import paho.mqtt.client as mqtt

HOST, PORT, USER, PWD = "120.79.3.248", 18883, "admin", "public"
DURATION = int(sys.argv[1]) if len(sys.argv) > 1 else 600
OUT = "H:/IoTPlatform/tools/watch_imei.jsonl"

imeis, wills, total = {}, [], 0
fh = open(OUT, "a", encoding="utf-8")


def on_connect(c, u, f, rc, props=None):
    print(f"[connect] rc={rc}", flush=True)
    for flt in ("#", "/iot/server/#"):
        c.subscribe(flt, qos=1)
    print("[sub] # (全量守候中)", flush=True)


def on_message(c, u, msg):
    global total
    total += 1
    payload = msg.payload.decode("utf-8", errors="replace")
    stamp = time.strftime("%H:%M:%S")

    seg = msg.topic.rstrip("/").split("/")[-1]
    found = seg if re.fullmatch(r"\d{14,17}", seg) else None

    method = None
    try:
        obj = json.loads(payload)
        method = obj.get("method")
        if not found and isinstance(obj.get("imei"), str):
            found = obj["imei"]
    except Exception:
        pass

    if found and found not in imeis:
        imeis[found] = msg.topic
        print(f"\n  ★★★ 发现 IMEI: {found}   topic={msg.topic}\n", flush=True)

    if method == "close":
        wills.append((stamp, msg.topic, payload))
        print(f"  ◆◆◆ 遗嘱 close 报文! {stamp} topic={msg.topic} :: {payload}", flush=True)

    print(f"[{stamp}] {msg.topic} method={method} :: {payload[:300]}", flush=True)
    fh.write(json.dumps({"ts": stamp, "topic": msg.topic, "raw": payload}, ensure_ascii=False) + "\n")
    fh.flush()


cli = mqtt.Client(client_id=f"watch_{int(time.time()) % 100000}", protocol=mqtt.MQTTv311)
cli.username_pw_set(USER, PWD)
cli.on_connect = on_connect
cli.on_message = on_message
cli.connect(HOST, PORT, 30)
cli.loop_start()

for i in range(DURATION):
    time.sleep(1)
    if i % 30 == 29:
        print(f"  ... 守候 {i + 1}s / {DURATION}s，报文 {total} 条，IMEI {len(imeis)} 个", flush=True)

cli.loop_stop()
cli.disconnect()
fh.close()

print("\n===== 守候结束 =====", flush=True)
print(f"报文总数: {total}", flush=True)
print(f"发现 IMEI: {list(imeis.keys()) or '(无)'}", flush=True)
print(f"遗嘱报文: {len(wills)} 条", flush=True)
for w in wills:
    print(f"  {w[0]} {w[1]} :: {w[2]}", flush=True)
