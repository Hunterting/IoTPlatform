"""通过 $SYS 分支判断 broker 上当前有多少客户端在线，区分“设备离线”与“设备在线但静默”。"""
import time

import paho.mqtt.client as mqtt

HOST, PORT, USER, PWD = "120.79.3.248", 18883, "admin", "public"
got = {}


def on_connect(c, u, f, rc, props=None):
    print(f"[connect] rc={rc}", flush=True)
    c.subscribe("$SYS/#", qos=0)


def on_subscribe(c, u, mid, granted_qos, props=None):
    q = list(granted_qos)
    print(f"[SUBACK] $SYS/# granted={q}{' ★被拒绝' if 128 in q else ''}", flush=True)


def on_message(c, u, msg):
    t = msg.topic
    if any(k in t for k in ("clients", "connected", "version", "uptime", "messages/received")):
        val = msg.payload.decode("utf-8", errors="replace")
        if got.get(t) != val:
            got[t] = val
            print(f"  {t} = {val}", flush=True)


cli = mqtt.Client(client_id=f"syschk_{int(time.time()) % 100000}", protocol=mqtt.MQTTv311)
cli.username_pw_set(USER, PWD)
cli.on_connect = on_connect
cli.on_subscribe = on_subscribe
cli.on_message = on_message
cli.connect(HOST, PORT, 30)
cli.loop_start()
time.sleep(12)
cli.loop_stop()
cli.disconnect()

print(f"\n$SYS 条目数: {len(got)}", flush=True)
if not got:
    print("broker 未开放 $SYS（EMQX 默认关闭），无法据此判断设备在线状态。", flush=True)
