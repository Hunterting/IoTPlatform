"""全量嗅探：订阅 # 根通配，判定设备是否在发报文、发到哪个 topic。"""
import json
import sys
import time

import paho.mqtt.client as mqtt

HOST, PORT, USER, PWD = "120.79.3.248", 18883, "admin", "public"
DURATION = int(sys.argv[1]) if len(sys.argv) > 1 else 75
hits = []


def on_connect(c, u, f, rc, props=None):
    print(f"[connect] rc={rc}", flush=True)
    c.subscribe("#", qos=1)
    c.subscribe("$SYS/broker/clients/#", qos=0)
    print("[sub] # + $SYS/broker/clients/#", flush=True)


def on_message(c, u, msg):
    try:
        payload = msg.payload.decode("utf-8", errors="replace")
    except Exception:
        payload = repr(msg.payload)
    rec = {"ts": time.strftime("%H:%M:%S"), "topic": msg.topic, "raw": payload}
    hits.append(rec)
    print(f"[MSG] {rec['ts']} topic={msg.topic!r} :: {payload[:400]}", flush=True)


cli = mqtt.Client(client_id=f"sniff_{int(time.time()) % 100000}", protocol=mqtt.MQTTv311)
cli.username_pw_set(USER, PWD)
cli.on_connect = on_connect
cli.on_message = on_message
cli.connect(HOST, PORT, 30)
cli.loop_start()

for i in range(DURATION):
    time.sleep(1)
    if i % 15 == 14:
        print(f"  ... {i + 1}s, 累计 {len(hits)} 条", flush=True)

cli.loop_stop()
cli.disconnect()

print("\n===== 汇总 =====", flush=True)
print(f"总报文数: {len(hits)}", flush=True)
topics = {}
for h in hits:
    topics[h["topic"]] = topics.get(h["topic"], 0) + 1
for t, n in sorted(topics.items()):
    print(f"  {t}  x{n}", flush=True)

with open("H:/IoTPlatform/tools/sniff_all.jsonl", "w", encoding="utf-8") as fh:
    for h in hits:
        fh.write(json.dumps(h, ensure_ascii=False) + "\n")
print("已写入 tools/sniff_all.jsonl", flush=True)
