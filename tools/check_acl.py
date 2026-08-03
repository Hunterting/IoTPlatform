"""校验 broker 是否真的授予了通配订阅权限（SUBACK granted QoS != 0x80）。"""
import time

import paho.mqtt.client as mqtt

HOST, PORT, USER, PWD = "120.79.3.248", 18883, "admin", "public"
FILTERS = [
    "#",
    "/iot/server/#",
    "/iot/server/iot-board/+",
    "/iot/client/#",
]
results = {}
mid_map = {}


def on_connect(c, u, f, rc, props=None):
    print(f"[connect] rc={rc} (0=成功)", flush=True)
    for flt in FILTERS:
        r = c.subscribe(flt, qos=1)
        mid_map[r[1]] = flt


def on_subscribe(c, u, mid, granted_qos, props=None):
    flt = mid_map.get(mid, f"mid={mid}")
    q = list(granted_qos)
    ok = all(x != 128 for x in q)
    results[flt] = q
    print(f"[SUBACK] {flt:32s} granted={q} {'授权通过' if ok else '★ 被 ACL 拒绝 (0x80)'}", flush=True)


def on_message(c, u, msg):
    print(f"[MSG] {msg.topic} :: {msg.payload[:200]}", flush=True)


cli = mqtt.Client(client_id=f"aclchk_{int(time.time()) % 100000}", protocol=mqtt.MQTTv311)
cli.username_pw_set(USER, PWD)
cli.on_connect = on_connect
cli.on_subscribe = on_subscribe
cli.on_message = on_message

# 自发自收回环：验证本连接确实能收到自己发布的消息
cli.connect(HOST, PORT, 30)
cli.loop_start()
time.sleep(3)

probe_topic = "/iot/server/iot-board/999999999999999"
print(f"\n[loopback] 自发自收测试 -> {probe_topic}", flush=True)
cli.publish(probe_topic, '{"imei":"999999999999999","method":"loopbackProbe"}', qos=1)
time.sleep(4)

cli.loop_stop()
cli.disconnect()
print("\n结论：若 loopback 报文被自己收到，说明订阅链路正常，0 上行 = 设备真的没发。", flush=True)
