\[TOC]



| 4G喇叭 | 4G开关 | WiFi喇叭 | WiFi开关 |

| --- | --- | --- | --- |

| <font color="red">\*\*\&times;\*\*</font> | <font color="green">\*\*\&radic;\*\*</font> | <font color="red">\*\*\&times;\*\*</font> | <font color="red">\*\*\&times;\*\*</font> |

支持此功能的设备类型：<font color="green">\*\*\&radic;\*\*</font>-支持 <font color="red">\*\*\&times;\*\*</font>-不支持



\## 获取流量信息（getFlowInfo）



\*\*简要描述\*\*

获取流量信息。



\*\*命令参数\*\*



| 参数名 | 必须 | 类型 | 说明 |

| --- | --- | --- | --- |

| method | 是 | string | getFlowInfo |

| frameId | 否 | string | 帧ID |



\*\*命令示例\*\*

```

{

&#x20; "method": "getFlowInfo",

&#x20; "frameId": "1745396239780"

}

```



\*\*应答参数说明\*\*



| 参数名 | 类型 | 说明 |

| --- | --- | --- |

| method | string | getFlowInfo |

| result | string | 返回结果。`ok`-成功；其他-具体失败原因 |

| iFlow | float | 瞬时流量，单位L/min |

| tFlow | float | 总流量，单位L |

| tPulses | int | 总脉冲计数 |

| imei | string | 设备imei |

| frameId | string | 同命令`frameId` |

| timestamp | int | 秒级时间戳，WiFi款不支持 |



\*\*应答示例\*\*

```

{

&#x20; "method": "getFlowInfo",

&#x20; "result": "ok",

&#x20; "iFLow": 12.603,

&#x20; "tFLow": 5.301,

&#x20; "tPulses": 203,

&#x20; "imei": "1745396239780",

&#x20; "frameId": "1745396239780",

&#x20; "timestamp": 1745396759

}

```



\*\*备注\*\*

无





\## 重置总流量（resetTotalFlow）



\*\*简要描述\*\*

重置总流量，将总流量清零。



\*\*命令参数\*\*



| 参数名 | 必须 | 类型 | 说明 |

| --- | --- | --- | --- |

| method | 是 | string | resetTotalFlow |

| frameId | 否 | string | 帧ID |



\*\*命令示例\*\*

```

{

&#x20; "method": "resetTotalFlow",

&#x20; "frameId": "1745396239780"

}

```



\*\*应答参数说明\*\*



| 参数名 | 类型 | 说明 |

| --- | --- | --- |

| method | string | resetTotalFlow |

| result | string | 返回结果。`ok`-成功；其他-具体失败原因 |

| imei | string | 设备imei |

| frameId | string | 同命令`frameId` |

| timestamp | int | 秒级时间戳，WiFi款不支持 |



\*\*应答示例\*\*

```

{

&#x20; "method": "resetTotalFlow",

&#x20; "result": "ok",

&#x20; "imei": "1745396239780",

&#x20; "frameId": "1745396239780",

&#x20; "timestamp": 1745396759

}

```



\*\*备注\*\*

无







\## 获取流量配置（getFlowConfig）



\*\*简要描述\*\*

获取流量配置。

通用公式：F = k × Q + offset，其中F为脉冲频率(Hz)，Q为流量(L/min)



\*\*命令参数\*\*



| 参数名 | 必须 | 类型 | 说明 |

| --- | --- | --- | --- |

| method | 是 | string | getFlowConfig |

| frameId | 否 | string | 帧ID |



\*\*命令示例\*\*

```

{

&#x20; "method": "getFlowConfig",

&#x20; "frameId": "1745396239780"

}

```



\*\*应答参数说明\*\*



| 参数名 | 类型 | 说明 |

| --- | --- | --- |

| method | string | getFlowConfig |

| result | string | 返回结果。`ok`-成功；其他-具体失败原因 |

| k | float | 系数，默认值-`11` |

| offset | float | 偏移量，默认值-`0`。例如通用公式：`F = 11 × Q - 4`，那么此时offset取值`-4` |

| minFlow | float | 有效范围最小值，单位L/min，小于此值舍去，默认值-`1` |

| maxFlow | float | 有效范围最大值，单位L/min，大于此值将取此值，默认值-`25` |

| imei | string | 设备imei |

| frameId | string | 同命令`frameId` |

| timestamp | int | 秒级时间戳，WiFi款不支持 |



\*\*应答示例\*\*

```

{

&#x20; "method": "getFlowConfig",

&#x20; "result": "ok",

&#x20; "k": 10,

&#x20; "offset": 0,

&#x20; "minFlow": 1,

&#x20; "maxFlow": 25,

&#x20; "imei": "1745396239780",

&#x20; "frameId": "1745396239780",

&#x20; "timestamp": 1745396759

}

```



\*\*备注\*\*

无





\## 设置流量配置（setFlowConfig）



\*\*简要描述\*\*

设置流量配置。

通用公式：F = k × Q + offset，其中F为脉冲频率(Hz)，Q为流量(L/min)



\*\*命令参数\*\*



| 参数名 | 必须 | 类型 | 说明 |

| --- | --- | --- | --- |

| method | 是 | string | setFlowConfig |

| k | 否  | float | 系数，默认值-`11` |

| offset | 否  | float | 偏移量，默认值-`0`。例如通用公式：`F = 11 × Q - 4`，那么此时offset取值`-4` |

| minFlow | 否  | float | 有效范围最小值，单位L/min，小于此值舍去，默认值-`1` |

| maxFlow | 否  | float | 有效范围最大值，单位L/min，大于此值将取此值，默认值-`25` |

| frameId | 否 | string | 帧ID |



\*\*命令示例\*\*

```

{

&#x20; "method": "setFlowConfig",

&#x20; "k": 10,

&#x20; "offset": 0,

&#x20; "minFlow": 1,

&#x20; "maxFlow": 25,

&#x20; "frameId": "1745396239780"

}

```



\*\*应答参数说明\*\*



| 参数名 | 类型 | 说明 |

| --- | --- | --- |

| method | string | setFlowConfig |

| result | string | 返回结果。`ok`-成功；其他-具体失败原因 |

| k | float | 系数，默认值-`11` |

| offset | float | 偏移量，默认值-`0`。例如通用公式：`F = 11 × Q - 4`，那么此时offset取值`-4` |

| minFlow | float | 有效范围最小值，单位L/min，小于此值舍去，默认值-`1` |

| maxFlow | float | 有效范围最大值，单位L/min，大于此值将取此值，默认值-`25` |

| imei | string | 设备imei |

| frameId | string | 同命令`frameId` |

| timestamp | int | 秒级时间戳，WiFi款不支持 |



\*\*应答示例\*\*

```

{

&#x20; "method": "setFlowConfig",

&#x20; "result": "ok",

&#x20; "k": 10,

&#x20; "offset": 0,

&#x20; "minFlow": 1,

&#x20; "maxFlow": 25,

&#x20; "imei": "1745396239780",

&#x20; "frameId": "1745396239780",

&#x20; "timestamp": 1745396759

}

```



\*\*备注\*\*

无





\## 重置流量配置（resetFlowConfig）



\*\*简要描述\*\*

重置流量配置为默认参数。

通用公式：F = k × Q + offset，其中F为脉冲频率(Hz)，Q为流量(L/min)



\*\*命令参数\*\*



| 参数名 | 必须 | 类型 | 说明 |

| --- | --- | --- | --- |

| method | 是 | string | getFlowConfig |

| frameId | 否 | string | 帧ID |



\*\*命令示例\*\*

```

{

&#x20; "method": "resetFlowConfig",

&#x20; "frameId": "1745396239780"

}

```



\*\*应答参数说明\*\*



| 参数名 | 类型 | 说明 |

| --- | --- | --- |

| method | string | resetFlowConfig |

| result | string | 返回结果。`ok`-成功；其他-具体失败原因 |

| k | float | 系数，默认值-`11` |

| offset | float | 偏移量，默认值-`0`。例如通用公式：`F = 11 × Q - 4`，那么此时offset取值`-4` |

| minFlow | float | 有效范围最小值，单位L/min，小于此值舍去，默认值-`1` |

| maxFlow | float | 有效范围最大值，单位L/min，大于此值将取此值，默认值-`25` |

| imei | string | 设备imei |

| frameId | string | 同命令`frameId` |

| timestamp | int | 秒级时间戳，WiFi款不支持 |



\*\*应答示例\*\*

```

{

&#x20; "method": "resetFlowConfig",

&#x20; "result": "ok",

&#x20; "k": 10,

&#x20; "offset": 0,

&#x20; "minFlow": 1,

&#x20; "maxFlow": 25,

&#x20; "imei": "1745396239780",

&#x20; "frameId": "1745396239780",

&#x20; "timestamp": 1745396759

}

```



\*\*备注\*\*

无





