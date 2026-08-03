\[TOC]
\## 二开设备MQTT参数配置说明
1\. 确保设备是默认出厂设置（如果是通过自己的服务器可以发送命令的则不需要出厂设置），且已联网（联网判断方式：可以通过发送`getDevInfo`等命令确保设备有应答，或通过我司的\[测试页面](https://app.ssc.asinfozz.com/iot/board/switch/test "测试页面")，可以获取到设备的基础信息或设备状态信息）。
2\. 所有参数都必须填写，不能为空。
3\. `host`为ip或域名，如`mqtt.example.com`或`220.152.36.102`，不要带`http://`、`https://`等前缀。
4\. `clientID`最好带有`%imei%`的，`%imei%`设备会替换成设备的imei，确保唯一性。
5\. `cleanSession`最好设置为`true`。
6\. `subscribeTopic`不能和`publishTopic`、`willTopic`一样。
7\. qos推荐为1。
8\. 复制时注意不要复制空格回车换行等不可见字符。
9\. `clientID`、`subscribeTopic`、`publishTopic`、`willTopic`、`will`支持`%imei%`替换语法，`%imei%`设备会替换成设备的imei。
\## MQTT参数配置例子
\- 例子一
```
{"host":"xxxxx","port":1883,"username":"xxxxx","password":"xxxxx","clientID":"%imei%","keepAlive":30,"cleanSession":true,"publishTopic":"/iot/server/iot-board","publishQos":1,"publishRetain":false,"subscribeTopic":"/iot/client/iot-board/%imei%","subscribeQos":1,"willTopic":"/iot/server/iot-board","willQos":1,"willRetain":false,"will":"{\\"imei\\":\\"%imei%\\",\\"method\\": \\"close\\"}"}
```
其中
`subscribeTopic`为
/iot/client/iot-board/%imei%
其中%imei%会替换成设备实际的imei。
`subscribeTopic`是设备订阅的主题，用来接收发送给设备的命令。
`publishTopic`、`willTopic`为
/iot/server/iot-board
`publishTopic`是设备发布的主题，用来发送设备执行完命令后的应答，还有设备事件(比如按键事件、订单结束事件等)的上报。
设备发布的应答中，都会带有设备实际的imei，可以用imei来区分是哪个设备的应答。
`willTopic`是设备的遗嘱主题，设备离线后，mqtt服务器会将`will`遗嘱发送给订阅了`willTopic`的软件客户端。
\- 例子二
```
{"host":"xxxxx","port":1883,"username":"xxxxx","password":"xxxxx","clientID":"%imei%","keepAlive":30,"cleanSession":true,"publishTopic":"/iot/server/iot-board/%imei%","publishQos":1,"publishRetain":false,"subscribeTopic":"/iot/client/iot-board/%imei%","subscribeQos":1,"willTopic":"/iot/server/iot-board/%imei%","willQos":1,"willRetain":false,"will":"{\\"imei\\":\\"%imei%\\",\\"method\\": \\"close\\"}"}
```
参数大致和例子一相同，不同的是`publishTopic`、`willTopic`都加上了`%imei%`。
软件客户端可以
1\. 通过订阅每一个设备的`publishTopic`，来处理不同设备的应答。
2\. 通过订阅主题通配符，比如/iot/server/iot-board/+，然后根据主题来区分设备。
\## 设备通信协议
\*\*1. 命令和应答采用json格式。\*\*
\*\*2. 后续命令说明json格式为了方便展示没有压缩，为了节省流量，生产环境最好使用压缩后的json。\*\*
\- 没有压缩例子
```
&#x20; {
&#x20;   "method": "getDevInfo",
&#x20;   "frameId": "1745396239780"
&#x20; }
```
\- 压缩例子
```
{"method":"getDevInfo","frameId":"1745396239780"}
```
\*\*3. json命令和应答中：\*\*
| 参数名 | 类型 | 说明 |
| --- | --- | --- |
| method | string | 命令名称 |
| result | string | 返回结果。`ok`-成功；`method unsupported`-设备暂不支持此命令；其他-具体失败原因 |
| imei | string | 设备imei |
| frameId | string | 帧ID/消息ID。设备返回的响应数据中的`frameId`会和下发给设备的命令数据中的`frameId`一样，用来表明当前的响应数据对应哪条命令。该值内容一般用时间戳的字符串(如`1767078752773`)，或递增的数值字符串(如`00001`)。 |
| timestamp | int | 秒级时间戳，WiFi款不支持 |
\*\*4. json命令和应答例子\*\*
\- 命令例子
```
&#x20; {
&#x20;   "method": "getDevInfo",
&#x20;   "frameId": "1745396239780"
&#x20; }
```
\- 应答例子
```
&#x20; {
&#x20;   "method": "getDevInfo",
&#x20;   "result": "ok",
&#x20;   "version": "SWITCH-EC618X-R24-O-V4.0.8",
&#x20;   "slotAmount": 1,
&#x20;   "phaseAmount": 1,
&#x20;   "imei": "1745396239780",
&#x20;   "frameId": "1745396239780",
&#x20;   "timestamp": 1745396759
&#x20; }
```
\*\*5. 一次给一台设备发送多个命令，每个命令之间最好间隔100ms，防止命令粘连\*\*
\[TOC] 
\## 通用命令
| 4G喇叭 | 4G开关 | WiFi喇叭 | WiFi开关 |
| --- | --- | --- | --- |
| <font color="green">\*\*\&radic;\*\*</font> | <font color="green">\*\*\&radic;\*\*</font> | <font color="green">\*\*\&radic;\*\*</font> | <font color="green">\*\*\&radic;\*\*</font> |
支持此功能的设备类型：<font color="green">\*\*\&radic;\*\*</font>-支持 <font color="red">\*\*\&times;\*\*</font>-不支持
\## 获取设备基本信息（getDevInfo）
\*\*简要描述\*\*
获取设备基本信息，如固件版本号等。
\*\*命令参数\*\*
| 参数名 | 必须 | 类型 | 说明 |
| --- | --- | --- | --- |
| method | 是 | string | getDevInfo |
| frameId | 否 | string | 帧ID |
\*\*命令示例\*\*
```
&#x20; {
&#x20;   "method": "getDevInfo",
&#x20;   "frameId": "1745396239780"
&#x20; }
```
\*\*应答参数说明\*\*
| 参数名 | 类型 | 说明 |
| --- | --- | --- |
| method | string | getDevInfo |
| result | string | 返回结果。`ok`-成功；其他-具体失败原因 |
| version | string | 版本号 |
| slotAmount | int | 插槽数量，开关类设备支持 |
| phaseAmount | int | 相位数量，开关类设备支持 |
| imei | string | 设备imei |
| frameId | string | 同命令`frameId` |
| timestamp | int | 秒级时间戳，WiFi款不支持 |
\*\*应答示例\*\*
```
{
&#x20; "method": "getDevInfo",
&#x20; "result": "ok",
&#x20; "version": "SWITCH-EC618X-R24-O-V4.0.8",
&#x20; "slotAmount": 1,
&#x20; "phaseAmount": 1,
&#x20; "imei": "1745396239780",
&#x20; "frameId": "1745396239780",
&#x20; "timestamp": 1745396759
}
```
\*\*备注\*\*
无
\## 获取设备实时状态信息（getDevStatus）
\*\*简要描述\*\*
获取设备实时状态信息，如固件版本号等。
\*\*命令参数\*\*
| 参数名 | 必须 | 类型 | 说明 |
| --- | --- | --- | --- |
| method | 是 | string | getDevStatus |
| q | 否 | string | 查询字符串，不传或为空则返回所有数据。有值则返回指定数据，目前仅支持`slots`,`EMdata`,`tasks`。比如"slots,EMdata"，表示该命令返回的数据包括`slots`,`EMdata`，`tasks`不返回。此字段用于节省数据通讯流量（v4.0.20及以上版本支持） |
| frameId | 否 | string | 帧ID |
\*\*命令示例\*\*
```
{
&#x20; "method": "getDevStatus",
&#x20; "frameId": "1745396239780"
}
```
\*\*应答参数说明\*\*
| 参数名 | 类型 | 说明 |
| --- | --- | --- |
| method | string | getDevStatus |
| result | string | 返回结果。`ok`-成功；其他-具体失败原因 |
| netType | string | 联网类型。`4G`、`WiFi` |
| iccid | string | 物联卡ICCID，4G款支持 |
| signal | int | 信号强度，1-31；4G款信号需要至少大于10，否则经常掉线，建议换个地方 |
| temperature | float | 温度 |
| gps | string | gps，格式：经度,纬度 |
| slots | array | 插槽状态int数组，按顺序从插槽1到插槽n，子项值：`0`-关闭;`1`-打开 |
| tasks | array | 插槽订单任务对象数组 |
| EMdata | array | 插槽电量计对象数组，按顺序从插槽1到插槽n |
| imei | string | 设备imei |
| frameId | string | 同命令`frameId` |
| timestamp | int | 秒级时间戳，WiFi款不支持 |
\- `tasks`数组对象说明
| key | 类型 | 说明 |
| --- | --- | --- |
| slotNum | int | 插槽编号，从1开始 |
| type | string | 订单类型，`TIME`-计时；`POWER`-计量 |
| status | string | 订单状态，`idle`-空闲/结束；`working`-进行中 |
| timeSec | int | 计时秒数。该参数在`type`为`TIME`时有效 |
| powerKwh | double | 计量电量，单位度。该参数在`type`为`POWER`时有效 |
| powerMaxSec | int | 计量最大秒数，总运行秒数到达计量最大秒数，任务停止。0为不限制。该参数在`type`为`POWER`时有效 |
| maxPower | int | 最大功率，单位W。超过最大功率，任务自动停止。0为使用设备默认值1400（不同设备有不同默认值） |
| pullOutStop | bool | 拔出自停。`true`-启用；`false`-禁用 |
| pullOutStopPower | int | 拔出自停功率，在拔出自停启用，且当前功率小于拔出自停功率时，任务自动停止。`0`为使用设备默认拔出自停功率3（不同设备有不同默认值） |
| pullOutStopStartSec | int | 订单启动后拔出自停开始判断秒数，默认0秒 |
| chargeFullStop | bool | 充满自停。`true`-启用；`false`-禁用 |
| chargeFullStopPower | int | 充满自停功率。`0`为使用设备默认值5（不同设备有不同默认值） |
| chageFullStopSec | int | 充满自停秒数，在充满自停启用，且当前功率小于充满自停功率并持续充满自停秒数后，任务自动停止。`0`为使用设备默认值60（不同设备有不同默认值） |
| chargeFullStopStartSec | int | 订单启动后充满自停开始判断秒数，默认0秒 |
| remark | string | 订单备注，启动订单时传入，可用于记录订单编号 |
| closeReason | string | 关闭原因 |
| totalSec | int | 总运行秒数 |
| totalKwh | double | 总运行度数 |
| voltage | double | 有效电压，单位V |
| current | double | 有效电流，单位A |
| power | double | 有效功率，单位W |
| vs | array | 多相电有效电压double数组，按顺序从1-n相，单位V，多相电设备才有 |
| cs | array | 多相电有效电流double数组，按顺序从1-n相，单位A，多相电设备才有 |
| ps | array | 多相电有效功率double数组，按顺序从1-n相，单位W，多相电设备才有 |
\- `closeReason`说明
| value | 说明 |
| --- | --- |
| CLOSED | 任务完成，定时任务`totalSec`达到`timeSec`；计量任务`totalKwh`达到`powerKwh` |
| MANUAL\_CLOSED | 下发结束订单命令关闭 |
| PULL\_OUT\_STOP\_CLOSE | 拔出自停，`pullOutStop`为`true`且满足拔出自停条件 |
| CHARGE\_FULL\_STOP\_CLOSE | 充满自停，`chargeFullStop`为`true`且满足拔出自停条件 |
| OVER\_POWER\_CLOSE | 超出最大功率停止，`power`持续超出`maxPower` |
| OVER\_TEMPERATURE\_CLOSE | 超出最大温度停止，`temperature`持续超出设备报警温度（不同设备有不同报警温度值） |
| REACH\_MAX\_TIME\_CLOSE | 达到最长时长关闭，计量任务`totalSec`达到`powerMaxSec` |
\- `EMdata`数组对象说明
| key | 类型 | 说明 |
| --- | --- | --- |
| v | double | 有效电压，单位V |
| c | double | 有效电流，单位A |
| p | double | 有效功率，单位W |
| e | double | 插槽总运行度数，单位度（非插槽订单任务总运行度数） |
\*\*应答示例\*\*
```
{
&#x20; "imei": "864536072949900",
&#x20; "gps": "113.2170916,023.4001628",
&#x20; "method": "getDevStatus",
&#x20; "timestamp": 1745398605,
&#x20; "signal": 25,
&#x20; "result": "ok",
&#x20; "model": "Air780E",
&#x20; "EMdata": \[
&#x20;   {
&#x20;     "c": 0.067,
&#x20;     "v": 239.0090179,
&#x20;     "p": 2.9530001,
&#x20;     "e": 0
&#x20;   }
&#x20; ],
&#x20; "slots": \[
&#x20;   0
&#x20; ],
&#x20; "netType": "4G",
&#x20; "iccid": "898608481024C0310590",
&#x20; "tasks": \[
&#x20;   {
&#x20;     "chargeFullStop": false,
&#x20;     "pullOutStopStartSec": 0,
&#x20;     "timeSec": 23596,
&#x20;     "voltage": "239.009",
&#x20;     "slotNum": 1,
&#x20;     "totalKwh": "0.000",
&#x20;     "pullOutStop": false,
&#x20;     "pullOutStopPower": 5,
&#x20;     "chargeFullStopPower": 10,
&#x20;     "remark": "QQO20250417092634",
&#x20;     "chargeFullStopStartSec": 0,
&#x20;     "closeReason": "CLOSED",
&#x20;     "current": "0.067",
&#x20;     "type": "TIME",
&#x20;     "power": "2.953",
&#x20;     "status": "idle",
&#x20;     "chargeFullStopSec": 60,
&#x20;     "maxPower": 2000,
&#x20;     "totalSec": 23596,
&#x20;     "ps": \[1.8430001,1.3590001,1.868],
&#x20;     "cs": \[0.045,0.019,0.026],
&#x20;     "vs": \[237.0470123,235.8450165,237.0630188]
&#x20;   }
&#x20; ],
&#x20; "temperature": "32.4",
&#x20; "frameId": "1745398603262"
}
```
\*\*备注\*\*
无
\## 设备连接MQTT成功事件上报（connected）
\*\*简要描述\*\*
设备连接MQTT成功触发事件。
\*\*命令参数\*\*
无
\*\*命令示例\*\*
无
\*\*应答参数说明\*\*
| 参数名 | 类型 | 说明 |
| --- | --- | --- |
| method | string | connected |
| imei | string | 设备imei |
| timestamp | int | 秒级时间戳，WiFi款不支持 |
\*\*应答示例\*\*
```
{
&#x20; "method": "connected",
&#x20; "imei": "1745396239780",
&#x20; "timestamp": 1745396759
}
```
\*\*备注\*\*
无
\## 按键事件上报（keyEvent）
\*\*简要描述\*\*
设备单击按键时触发。
\*\*命令参数\*\*
无
\*\*命令示例\*\*
无
\*\*应答参数说明\*\*
| 参数名 | 类型 | 说明 |
| --- | --- | --- |
| method | string | keyEvent |
| imei | string | 设备imei |
| timestamp | int | 秒级时间戳，WiFi款不支持 |
\*\*应答示例\*\*
```
{
&#x20; "method": "keyEvent",
&#x20; "imei": "1745396239780",
&#x20; "timestamp": 1745396759
}
```
\*\*备注\*\*
无
\## 获取按键配置（getKeyConfig）
\*\*简要描述\*\*
获取按键配置。
\*\*命令参数\*\*
| 参数名 | 必须 | 类型 | 说明 |
| --- | --- | --- | --- |
| method | 是 | string | getKeyConfig |
| frameId | 否 | string | 帧ID |
\*\*命令示例\*\*
```
{
&#x20; "method": "getKeyConfig",
&#x20; "frameId": "1745396239780"
}
```
\*\*应答参数说明\*\*
| 参数名 | 类型 | 说明 |
| --- | --- | --- |
| method | string | getKeyConfig |
| result | string | 返回结果。`ok`-成功；其他-具体失败原因 |
| mode | int | 按键模式。`0`-无动作；`1`-切换开关；`2`-离线切换开关，联网不动作 |
| uploadEnable | bool | 是否上报按键事件 |
| frameId | string | 同命令`frameId` |
| timestamp | int | 秒级时间戳，WiFi款不支持 |
\*\*应答示例\*\*
```
{
&#x20; "method": "getKeyConfig",
&#x20; "result": "ok",
  "mode": 1,
  "uploadEnable": true,
&#x20; "imei": "1745396239780",
&#x20; "frameId": "1745396239780",
&#x20; "timestamp": 1745396759
}
```
\*\*备注\*\*
无
\## 设置按键配置（setKeyConfig）
\*\*简要描述\*\*
设置按键配置。
\*\*命令参数\*\*
| 参数名 | 必须 | 类型 | 说明 |
| --- | --- | --- | --- |
| method | 是 | string | setKeyConfig |
| mode | 是 | int | 按键模式。`0`-无动作；`1`-切换开关；`2`-离线切换开关，联网不动作 |
| uploadEnable | 是 | bool | 是否上报按键事件 |
| frameId | 否 | string | 帧ID |
\*\*命令示例\*\*
```
{
&#x20; "method": "setKeyConfig",
  "mode": 1,
  "uploadEnable": true,
&#x20; "frameId": "1745396239780"
}
```
\*\*应答参数说明\*\*
| 参数名 | 类型 | 说明 |
| --- | --- | --- |
| method | string | setKeyConfig |
| result | string | 返回结果。`ok`-成功；其他-具体失败原因 |
| mode | int | 按键模式。`0`-无动作；`1`-切换开关；`2`-离线切换开关，联网不动作 |
| uploadEnable | bool | 是否上报按键事件 |
| frameId | string | 同命令`frameId` |
| timestamp | int | 秒级时间戳，WiFi款不支持 |
\*\*应答示例\*\*
```
{
&#x20; "method": "setKeyConfig",
&#x20; "result": "ok",
  "mode": 1,
  "uploadEnable": true,
&#x20; "imei": "1745396239780",
&#x20; "frameId": "1745396239780",
&#x20; "timestamp": 1745396759
}
```
\*\*备注\*\*
无
\## 重启（reboot）
\*\*简要描述\*\*
远程重启设备。
\*\*命令参数\*\*
| 参数名 | 必须 | 类型 | 说明 |
| --- | --- | --- | --- |
| method | 是 | string | reboot |
| frameId | 否 | string | 帧ID |
\*\*命令示例\*\*
```
{
&#x20; "method": "reboot",
&#x20; "frameId": "1745396239780"
}
```
\*\*应答参数说明\*\*
| 参数名 | 类型 | 说明 |
| --- | --- | --- |
| method | string | reboot |
| result | string | 返回结果。`ok`-成功；其他-具体失败原因 |
| imei | string | 设备imei |
| frameId | string | 同命令`frameId` |
| timestamp | int | 秒级时间戳，WiFi款不支持 |
\*\*应答示例\*\*
```
{
&#x20; "method": "reboot",
&#x20; "result": "ok",
&#x20; "imei": "1745396239780",
&#x20; "frameId": "1745396239780",
&#x20; "timestamp": 1745396759
}
```
\*\*备注\*\*
无
\## 获取自动上报配置（getAutoReport）（测试中）
\*\*简要描述\*\*
获取自动上报配置（v4.0.20及以上版本支持）。
\*\*命令参数\*\*
| 参数名 | 必须 | 类型 | 说明 |
| --- | --- | --- | --- |
| method | 是 | string | getAutoReport |
| frameId | 否 | string | 帧ID |
\*\*命令示例\*\*
```
{
&#x20; "method": "getAutoReport",
&#x20; "frameId": "1745396239780"
}
```
\*\*应答参数说明\*\*
| 参数名 | 类型 | 说明 |
| --- | --- | --- |
| method | string | getAutoReport |
| result | string | 返回结果。`ok`-成功；其他-具体失败原因 |
| getDevStatusSec | int | 设备实时状态自动上报间隔秒数。`0`-不上报，该值不为0时不能小于`30`秒 |
| getDevStatusQ | string | 设备实时状态自动上报查询字符串，不传或为空则返回所有数据。有值则返回指定数据，目前仅支持`slots`,`EMdata`,`tasks`。比如"slots,EMdata"，表示该命令返回的数据包括`slots`,`EMdata`，`tasks`不返回。此字段用于节省数据通讯流量 |
| orderUpSec | int | 订单数据自动上报间隔秒数。`0`-不上报，该值不为0时不能小于`30`秒 |
| rs485Sec | int | RS485自动上报间隔秒数。`0`-不上报，该值不为0时不能小于`30`秒 |
| rs485BaudRate | int | RS485自动上报串口波特率，`2400`\~`2000000`，默认`115200` |
| rs485SendWaitMs | int | RS485自动上报多个命令间隔毫秒数，默认`300` |
| rs485Array | array | RS485自动上报下发命令数组。十六进制命令字符串数组 |
| imei | string | 设备imei |
| frameId | string | 同命令`frameId` |
| timestamp | int | 秒级时间戳，WiFi款不支持 |
\*\*应答示例\*\*
```
{
&#x20; "method": "getAutoReport",
&#x20; "result": "ok",
&#x20; "getDevStatusSec": 600,
&#x20; "getDevStatusQ": "slots,EMdata",
&#x20; "orderUpSec": 0,
&#x20; "rs485Sec": 200,
&#x20; "rs485BaudRate": 115200,
&#x20; "rs485SendWaitMs": 300,
&#x20; "rs485Array": \["3837313131","3a4d558921"],
&#x20; "imei": "1745396239780",
&#x20; "frameId": "1745396239780",
&#x20; "timestamp": 1745396759
}
```
\*\*备注\*\*
无
\## 设置自动上报配置（setAutoReport）（测试中）
\*\*简要描述\*\*
设置自动上报配置（v4.0.20及以上版本支持）。
\*\*命令参数\*\*
| 参数名 | 必须 | 类型 | 说明 |
| --- | --- | --- | --- |
| method | 是 | string | setAutoReport |
| getDevStatusSec | 是 | int | 设备实时状态自动上报间隔秒数。`0`-不上报，该值不为0时不能小于`30`秒 |
| getDevStatusQ | 否 | string | 设备实时状态自动上报查询字符串，不传或为空则返回所有数据。有值则返回指定数据，目前仅支持`slots`,`EMdata`,`tasks`。比如"slots,EMdata"，表示该命令返回的数据包括`slots`,`EMdata`，`tasks`不返回。此字段用于节省数据通讯流量 |
| orderUpSec | 是 | int | 订单数据自动上报间隔秒数。`0`-不上报，该值不为0时不能小于`30`秒 |
| rs485Sec | 是 | int | RS485自动上报间隔秒数。`0`-不上报，该值不为0时不能小于`30`秒 |
| rs485BaudRate | 是 | int | RS485自动上报串口波特率，`2400`\~`2000000`，默认`115200` |
| rs485SendWaitMs | 否 | int | RS485自动上报多个命令间隔毫秒数，默认`300` |
| rs485Array | 否 | array | RS485自动上报下发命令数组。十六进制命令字符串数组 |
| frameId | 否 | string | 帧ID |
\*\*命令示例\*\*
```
{
&#x20; "method": "setAutoReport",
&#x20; "getDevStatusSec": 600,
&#x20; "getDevStatusQ": "slots,EMdata",
&#x20; "orderUpSec": 0,
&#x20; "rs485Sec": 200,
&#x20; "rs485BaudRate": 115200,
&#x20; "rs485SendWaitMs": 300,
&#x20; "rs485Array": \["3837313131","3a4d558921"],
&#x20; "frameId": "1745396239780"
}
```
\*\*应答参数说明\*\*
| 参数名 | 类型 | 说明 |
| --- | --- | --- |
| method | string | setAutoReport |
| result | string | 返回结果。`ok`-成功；其他-具体失败原因 |
| getDevStatusSec | int | 设备实时状态自动上报间隔秒数。`0`-不上报，该值不为0时不能小于`30`秒 |
| getDevStatusQ | string | 设备实时状态自动上报查询字符串，不传或为空则返回所有数据。有值则返回指定数据，目前仅支持`slots`,`EMdata`,`tasks`。比如"slots,EMdata"，表示该命令返回的数据包括`slots`,`EMdata`，`tasks`不返回。此字段用于节省数据通讯流量 |
| orderUpSec | int | 订单数据自动上报间隔秒数。`0`-不上报，该值不为0时不能小于`30`秒 |
| rs485Sec | int | RS485自动上报间隔秒数。`0`-不上报，该值不为0时不能小于`30`秒 |
| rs485BaudRate | int | RS485自动上报串口波特率，`2400`\~`2000000`，默认`115200` |
| rs485SendWaitMs | int | RS485自动上报多个命令间隔毫秒数，默认`300` |
| rs485Array | array | RS485自动上报下发命令数组。十六进制命令字符串数组 |
| imei | string | 设备imei |
| frameId | string | 同命令`frameId` |
| timestamp | int | 秒级时间戳，WiFi款不支持 |
\*\*应答示例\*\*
```
{
&#x20; "method": "setAutoReport",
&#x20; "result": "ok",
&#x20; "getDevStatusSec": 600,
&#x20; "getDevStatusQ": "slots,EMdata",
&#x20; "orderUpSec": 0,
&#x20; "rs485Sec": 200,
&#x20; "rs485BaudRate": 115200,
&#x20; "rs485SendWaitMs": 300,
&#x20; "rs485Array": \["3837313131","3a4d558921"],
&#x20; "imei": "1745396239780",
&#x20; "frameId": "1745396239780",
&#x20; "timestamp": 1745396759
}
```
\*\*备注\*\*
无
\[TOC]
| 4G喇叭 | 4G开关 | WiFi喇叭 | WiFi开关 |
| --- | --- | --- | --- |
| <font color="green">\*\*\&radic;\*\*</font> | <font color="green">\*\*\&radic;\*\*</font> | <font color="green">\*\*\&radic;\*\*</font> | <font color="green">\*\*\&radic;\*\*</font> |
支持此功能的设备类型：<font color="green">\*\*\&radic;\*\*</font>-支持 <font color="red">\*\*\&times;\*\*</font>-不支持
\## 获取MQTT参数（getMqtt）
\*\*简要描述\*\*
获取MQTT参数。
\*\*命令参数\*\*
| 参数名 | 必须 | 类型 | 说明 |
| --- | --- | --- | --- |
| method | 是 | string | getMqtt |
| frameId | 否 | string | 帧ID |
\*\*命令示例\*\*
```
{
&#x20; "method": "getMqtt",
&#x20; "frameId": "1745396239780"
}
```
\*\*应答参数说明\*\*
| 参数名 | 类型 | 说明 |
| --- | --- | --- |
| method | string | getMqtt |
| result | string | 返回结果。`ok`-成功；其他-具体失败原因 |
| mqttParams | object | mqtt参数对象 |
| imei | string | 设备imei |
| frameId | string | 同命令`frameId` |
| timestamp | int | 秒级时间戳，WiFi款不支持 |
\- mqttParams - mqtt参数对象说明
| 参数名 | 类型 | 说明 |
| --- | --- | --- |
| host | string | MQTT 服务器地址（如域名或 IP，例：mqtt.example.com） |
| port | int | MQTT 服务器端口（例：1883 或 8883（SSL）） |
| username | string | MQTT 服务器连接用户名 |
| password | string | MQTT 服务器连接密码 |
| clientID | string | 客户端唯一标识，`%imei%` 为设备 IMEI 动态替换字段 |
| cleanSession | bool | 是否启用干净会话（`true` 表示清除历史会话状态；`false` 表示保留） |
| keepAlive | int | 心跳间隔时间（秒），用于维持与服务器的连接 |
| subscribeTopic | string | 设备订阅消息的主题，`%imei%` 动态替换为设备 IMEI |
| subscribeQos | int | 订阅主题的 QoS 等级（0/1/2，WiFi设备只支持0/1） |
| publishTopic | string | 设备发布消息的主题，`%imei%` 动态替换为设备 IMEI |
| publishQos | int | 发布主题的 QoS 等级（0/1/2，WiFi设备只支持0/1） |
| publishRetain | bool | 发布消息时是否设为保留消息（`true` 表示保留，`false` 表示不保留） |
| willTopic | string | 遗嘱消息发送的主题，`%imei%` 动态替换为设备 IMEI |
| willQos | string | 遗嘱主题的 QoS 等级（0/1/2，WiFi设备只支持0/1） |
| willRetain | bool | 遗嘱消息是否设为保留消息（`true` 保留，`false` 不保留） |
| will | string | 遗嘱消息内容，`%imei%` 动态替换为设备 IMEI（设备异常离线时发送至 `willTopic`） |
| useSSL | bool | （内测中）是否启用SSL |
| caCert | string | （内测中）ca证书，如果为空，则用默认ca证书（常见的受信任的ca机构颁发的根证书），不为空则用设置的`caCert` |
| clientCert | string | （内测中）client证书，`clientCert`、`privateKey`为空则不启用双向认证 |
| privateKey | string | （内测中）密钥，`clientCert`、`privateKey`为空则不启用双向认证 |
\*\*应答示例\*\*
```
{
&#x20; "mqttParams": {
&#x20;   "password": "\*\*\*\*\*\*\*\*",
&#x20;   "host": "\*\*\*\*\*\*\*\*",
&#x20;   "clientID": "%imei%",
&#x20;   "publishRetain": false,
&#x20;   "cleanSession": true,
&#x20;   "username": "\*\*\*\*\*\*\*\*",
&#x20;   "willQos": 1,
&#x20;   "publishQos": 1,
&#x20;   "will": "{\\"method\\":\\"close\\",\\"imei\\":\\"%imei%\\"}",
&#x20;   "port": 10000,
&#x20;   "publishTopic": "/iot/server/iot-board/%imei%",
&#x20;   "subscribeTopic": "/iot/client/iot-board/%imei%",
&#x20;   "subscribeQos": 1,
&#x20;   "willRetain": false,
&#x20;   "willTopic": "/iot/server/iot-board/%imei%",
&#x20;   "keepAlive": 30,
&#x20;   "useSSL": false,
&#x20;   "caCert": "",
&#x20;   "clientCert": "",
&#x20;   "privateKey": ""
&#x20; },
&#x20; "imei": "864536072949900",
&#x20; "method": "getMqtt",
&#x20; "timestamp": 1745478194,
&#x20; "result": "ok",
&#x20; "frameId": "1745478194596"
}
```
\*\*备注\*\*
无
\## 设置MQTT参数（setMqtt）
\*\*简要描述\*\*
设置MQTT参数。
\*\*命令参数\*\*
| 参数名 | 必须 | 类型 | 说明 |
| --- | --- | --- | --- |
| method | 是 | string | setMqtt |
| mqttParams | 是 | object | mqtt参数对象 |
| reboot | 否 | bool | 设置完是否重启 |
| frameId | 否 | string | 帧ID |
\- mqttParams - mqtt参数对象说明
| 参数名 | 必须 | 类型 | 说明 |
| --- | --- | --- |
| host | 是 | string | MQTT 服务器地址（如域名或 IP，例：mqtt.example.com） |
| port | 是 | int | MQTT 服务器端口（例：1883 或 8883（SSL）） |
| username | 是 | string | MQTT 服务器连接用户名 |
| password | 是 | string | MQTT 服务器连接密码 |
| clientID | 是 | string | 客户端唯一标识，`%imei%` 为设备 IMEI 动态替换字段 |
| cleanSession | 是 | bool | 是否启用干净会话（`true` 表示清除历史会话状态；`false` 表示保留） |
| keepAlive | 是 | int | 心跳间隔时间（秒），用于维持与服务器的连接 |
| subscribeTopic | 是 | string | 设备订阅消息的主题，`%imei%` 动态替换为设备 IMEI |
| subscribeQos | 是 | int | 订阅主题的 QoS 等级（0/1/2，WiFi设备只支持0/1） |
| subTopics | 否 | array | 订阅主题数组，用来补充多订阅主题，需要lib库版本大于等于V1.2.0 |
| publishTopic | 是 | string | 设备发布消息的主题，`%imei%` 动态替换为设备 IMEI |
| publishQos | 是 | int | 发布主题的 QoS 等级（0/1/2，WiFi设备只支持0/1） |
| publishRetain | 是 | bool | 发布消息时是否设为保留消息（`true` 表示保留，`false` 表示不保留） |
| willTopic | 是 | string | 遗嘱消息发送的主题，`%imei%` 动态替换为设备 IMEI |
| willQos | 是 | string | 遗嘱主题的 QoS 等级（0/1/2，WiFi设备只支持0/1） |
| willRetain | 是 | bool | 遗嘱消息是否设为保留消息（`true` 保留，`false` 不保留） |
| will | 是 | string | 遗嘱消息内容，`%imei%` 动态替换为设备 IMEI（设备异常离线时发送至 `willTopic`） |
| useSSL | 否 | bool | （内测中）是否启用SSL |
| caCert | 否 | string | （内测中）ca证书，如果为空，则用默认ca证书（常见的受信任的ca机构颁发的根证书），不为空则用设置的`caCert` |
| clientCert | 否 | string | （内测中）client证书，`clientCert`、`privateKey`为空则不启用双向认证 |
| privateKey | 否 | string | （内测中）密钥，`clientCert`、`privateKey`为空则不启用双向认证 |
\- subTopics - 多订阅主题数组说明
| 参数名 | 必须 | 类型 | 说明 |
| --- | --- | --- |
| topic | 是 | string | 设备订阅消息的主题，`%imei%` 动态替换为设备 IMEI |
| qos | 是 | int | 订阅主题的 QoS 等级（0/1/2，WiFi设备只支持0/1） |
\*\*命令示例\*\*
```
{
&#x20; "method": "setMqtt",
&#x20; "reboot": true,
&#x20; "mqttParams": {
&#x20;   "host": "mqtt.xxxxxx.com",
&#x20;   "port": 8200,
&#x20;   "username": "test",
&#x20;   "password": "test",
&#x20;   "clientID": "clientID",
&#x20;   "publishTopic": "pubTopic",
&#x20;   "subscribeTopic": "subTopic",
&#x20;   "subTopics": \[{
&#x09;	"topic": "subTopic2",
&#x09;	"qos": 1
&#x09;},{
&#x09;	"topic": "subTopic3",
&#x09;	"qos": 0
&#x09;}],
&#x20;   "willTopic": "willTopic",
&#x20;   "will": "{\\"method\\":\\"close\\",\\"imei\\":\\"%imei%\\"}",
&#x20;   "keepAlive": 30,
&#x20;   "cleanSession": true,
&#x20;   "publishQos": 2,
&#x20;   "subscribeQos": 2,
&#x20;   "willQos": 2,
&#x20;   "publishRetain": false,
&#x20;   "willRetain": false,
&#x20;   "useSSL": false,
&#x20;   "caCert": "",
&#x20;   "clientCert": "",
&#x20;   "privateKey": ""
&#x20; },
&#x20; "method": "setMqtt",
&#x20; "frameId": "1745478194596"
}
```
\*\*应答参数说明\*\*
| 参数名 | 类型 | 说明 |
| --- | --- | --- |
| method | string | setMqtt |
| result | string | 返回结果。`ok`-成功；其他-具体失败原因 |
| imei | string | 设备imei |
| frameId | string | 同命令`frameId` |
| timestamp | int | 秒级时间戳，WiFi款不支持 |
\*\*应答示例\*\*
```
{
&#x20; "method": "setMqtt",
&#x20; "result": "ok",
&#x20; "imei": "1745396239780",
&#x20; "frameId": "1745396239780",
&#x20; "timestamp": 1745396759
}
```
\*\*备注\*\*
无
\[TOC]
| 4G喇叭 | 4G开关 | WiFi喇叭 | WiFi开关 |
| --- | --- | --- | --- |
| <font color="red">\*\*\&times;\*\*</font> | <font color="green">\*\*\&radic;\*\*</font> | <font color="red">\*\*\&times;\*\*</font> | <font color="green">\*\*\&radic;\*\*</font> |
支持此功能的设备类型：<font color="green">\*\*\&radic;\*\*</font>-支持 <font color="red">\*\*\&times;\*\*</font>-不支持
\## 插槽开关动作（action）
\*\*简要描述\*\*
插槽开关动作。
\*\*命令参数\*\*
| 参数名 | 必须 | 类型 | 说明 |
| --- | --- | --- | --- |
| method | 是 | string | action |
| slotNum | 是 | int | 插槽编号，从1开始。`0`表示所有插槽开关 |
| action | 是 | string | 开关动作。`on`-打开；`off`-关闭；`toggle`-翻转 |
| hasStopDelayTask | 否 | bool | 是否停止延时任务。`true`-是；`false`-否 |
| frameId | 否 | string | 帧ID |
\*\*命令示例\*\*
```
{
&#x20; "method": "action",
&#x20; "slotNum": 1,
&#x20; "action": "on",
&#x20; "hasStopDelayTask": false,
&#x20; "frameId": "1745396239780"
}
```
\*\*应答参数说明\*\*
| 参数名 | 类型 | 说明 |
| --- | --- | --- |
| method | string | action |
| result | string | 返回结果。`ok`-成功；其他-具体失败原因 |
| slots | array | 插槽状态int数组，按顺序从插槽1到插槽n，子项值：`0`-关闭;`1`-打开 |
| imei | string | 设备imei |
| frameId | string | 同命令`frameId` |
| timestamp | int | 秒级时间戳，WiFi款不支持 |
\*\*应答示例\*\*
```
{
&#x20; "method": "action",
&#x20; "result": "ok",
&#x20; "slots": \[0],
&#x20; "imei": "1745396239780",
&#x20; "frameId": "1745396239780",
&#x20; "timestamp": 1745396759
}
```
\*\*备注\*\*
无
\## 多插槽开关动作（actions）
\*\*简要描述\*\*
多插槽开关动作。
\*\*命令参数\*\*
| 参数名 | 必须 | 类型 | 说明 |
| --- | --- | --- | --- |
| method | 是 | string | actions |
| slotNums | 是 | array | 插槽编号数组，子项值从1开始。 |
| action | 是 | string | 开关动作。`on`-打开；`off`-关闭；`toggle`-翻转 |
| hasStopDelayTask | 否 | bool | 是否停止延时任务。`true`-是；`false`-否 |
| frameId | 否 | string | 帧ID |
\*\*命令示例\*\*
```
{
&#x20; "method": "actions",
&#x20; "slotNums": \[1,3,4],
&#x20; "action": "on",
&#x20; "hasStopDelayTask": false,
&#x20; "frameId": "1745396239780"
}
```
\*\*应答参数说明\*\*
| 参数名 | 类型 | 说明 |
| --- | --- | --- |
| method | string | actions |
| result | string | 返回结果。`ok`-成功；其他-具体失败原因 |
| slots | array | 插槽状态int数组，按顺序从插槽1到插槽n，子项值：`0`-关闭;`1`-打开 |
| imei | string | 设备imei |
| frameId | string | 同命令`frameId` |
| timestamp | int | 秒级时间戳，WiFi款不支持 |
\*\*应答示例\*\*
```
{
&#x20; "method": "actions",
&#x20; "result": "ok",
&#x20; "slots": \[1,3,4],
&#x20; "imei": "1745396239780",
&#x20; "frameId": "1745396239780",
&#x20; "timestamp": 1745396759
}
```
\*\*备注\*\*
无
\## 获取延时任务列表（getDelayTasks）
\*\*简要描述\*\*
获取插槽开关延时任务。
\*\*命令参数\*\*
| 参数名 | 必须 | 类型 | 说明 |
| --- | --- | --- | --- |
| method | 是 | string | getDelayTasks |
| frameId | 否 | string | 帧ID |
\*\*命令示例\*\*
```
{
&#x20; "method": "getDelayTasks",
&#x20; "frameId": "1745396239780"
}
```
\*\*应答参数说明\*\*
| 参数名 | 类型 | 说明 |
| --- | --- | --- |
| method | string | getDelayTasks |
| result | string | 返回结果。`ok`-成功；其他-具体失败原因 |
| tasks | array | 插槽开关延时任务数组，按顺序从插槽1到插槽n |
| imei | string | 设备imei |
| frameId | string | 同命令`frameId` |
| timestamp | int | 秒级时间戳，WiFi款不支持 |
\- tasks数组对象说明
| key | 类型 | 说明 |
| --- | --- | --- |
| enable | bool | 是否启用，`true`-是；`false`-否 |
| sAction | string | 开关开始动作。`on`-打开；`off`-关闭；`toggle`-翻转 |
| eAction | string | 开关延时结束动作。`on`-打开；`off`-关闭；`toggle`-翻转 |
| secs | int | 延时秒数 |
| cnt | int | 当前计数秒数 |
\*\*应答示例\*\*
```
{
&#x20; "method": "getDelayTasks",
&#x20; "result": "ok",
&#x20; "tasks": \[
&#x20;   {
&#x20;     "cnt": 7,
&#x20;     "eAction": "toggle",
&#x20;     "sAction": "none",
&#x20;     "secs": 100,
&#x20;     "enable": true
&#x20;   }
  ],
&#x20; "imei": "1745396239780",
&#x20; "frameId": "1745396239780",
&#x20; "timestamp": 1745396759
}
```
\*\*备注\*\*
无
\## 开始延时任务（startDelayTask）
\*\*简要描述\*\*
开始延时任务。
\*\*命令参数\*\*
| 参数名 | 必须 | 类型 | 说明 |
| --- | --- | --- | --- |
| method | 是 | string | startDelayTask |
| slotNum | 是 | int | 插槽编号，从1开始。`0`表示所有插槽开关 |
| enable | 是 | bool | 是否启用，`true`-是；`false`-否 |
| sAction | 是 | string | 开关开始动作。`on`-打开；`off`-关闭；`toggle`-翻转；`none`-无动作 |
| eAction | 是 | string | 开关延时结束动作。`on`-打开；`off`-关闭；`toggle`-翻转 |
| secs | 是 | int | 延时秒数 |
| frameId | 否 | string | 帧ID |
\*\*命令示例\*\*
```
{
&#x20; "method": "startDelayTask",
&#x20; "slotNum": 1,
&#x20; "sAction": "none",
&#x20; "secs": 100,
&#x20; "eAction": "toggle",
&#x20; "frameId": "1745396239780"
}
```
\*\*应答参数说明\*\*
| 参数名 | 类型 | 说明 |
| --- | --- | --- |
| method | string | startDelayTask |
| result | string | 返回结果。`ok`-成功；其他-具体失败原因 |
| imei | string | 设备imei |
| frameId | string | 同命令`frameId` |
| timestamp | int | 秒级时间戳，WiFi款不支持 |
\*\*应答示例\*\*
```
{
&#x20; "method": "startDelayTask",
&#x20; "result": "ok",
&#x20; "imei": "1745396239780",
&#x20; "frameId": "1745396239780",
&#x20; "timestamp": 1745396759
}
```
\*\*备注\*\*
无
\## 停止延时任务（stopDelayTask）
\*\*简要描述\*\*
停止延时任务。
\*\*命令参数\*\*
| 参数名 | 必须 | 类型 | 说明 |
| --- | --- | --- | --- |
| method | 是 | string | stopDelayTask |
| slotNum | 是 | int | 插槽编号，从1开始。`0`表示所有插槽开关 |
| frameId | 否 | string | 帧ID |
\*\*命令示例\*\*
```
{
&#x20; "method": "stopDelayTask",
&#x20; "slotNum": 1,
&#x20; "frameId": "1745396239780"
}
```
\*\*应答参数说明\*\*
| 参数名 | 类型 | 说明 |
| --- | --- | --- |
| method | string | stopDelayTask |
| result | string | 返回结果。`ok`-成功；其他-具体失败原因 |
| imei | string | 设备imei |
| frameId | string | 同命令`frameId` |
| timestamp | int | 秒级时间戳，WiFi款不支持 |
\*\*应答示例\*\*
```
{
&#x20; "method": "stopDelayTask",
&#x20; "result": "ok",
&#x20; "imei": "1745396239780",
&#x20; "frameId": "1745396239780",
&#x20; "timestamp": 1745396759
}
```
\*\*备注\*\*
无
\## 延时任务事件上报（delayEvent）
\*\*简要描述\*\*
延时任务结束触发事件。
\*\*命令参数\*\*
无
\*\*命令示例\*\*
无
\*\*应答参数说明\*\*
| 参数名 | 类型 | 说明 |
| --- | --- | --- |
| method | string | delayEvent |
| result | string | 返回结果。`ok`-成功；其他-具体失败原因 |
| slotNum | 是 | int | 插槽编号，从1开始 |
| slots | array | 插槽状态int数组，按顺序从插槽1到插槽n，子项值：`0`-关闭;`1`-打开 |
| imei | string | 设备imei |
| frameId | string | 同命令`frameId` |
| timestamp | int | 秒级时间戳，WiFi款不支持 |
\*\*应答示例\*\*
```
{
&#x20; "method": "delayEvent",
&#x20; "result": "ok",
&#x20; "slotNum": 1,
&#x20; "slots": \[0],
&#x20; "imei": "1745396239780",
&#x20; "frameId": "1745396239780",
&#x20; "timestamp": 1745396759
}
```
\*\*备注\*\*
无
\## 获取电量计实时信息（getEMRealtime）
\*\*简要描述\*\*
获取电量计实时信息。
\*\*命令参数\*\*
| 参数名 | 必须 | 类型 | 说明 |
| --- | --- | --- | --- |
| method | 是 | string | getEMRealtime |
| frameId | 否 | string | 帧ID |
\*\*命令示例\*\*
```
{
&#x20; "method": "getEMRealtime",
&#x20; "frameId": "1745396239780"
}
```
\*\*应答参数说明\*\*
| 参数名 | 类型 | 说明 |
| --- | --- | --- |
| method | string | getEMRealtime |
| result | string | 返回结果。`ok`-成功；其他-具体失败原因 |
| data | array | 插槽电量计对象数组，按顺序从插槽1到插槽n |
| imei | string | 设备imei |
| frameId | string | 同命令`frameId` |
| timestamp | int | 秒级时间戳，WiFi款不支持 |
\- `data`数组对象说明
| key | 类型 | 说明 |
| --- | --- | --- |
| v | double | 有效电压，单位V，（多相电设备时为多相电压平均值） |
| c | double | 有效电流，单位A，（多相电设备时为多相电流总和） |
| p | double | 有效功率，单位W，（多相电设备时为多相电功率总和） |
| e | double | 插槽总运行度数，单位度（非插槽订单任务总运行度数） |
| vs | array | 多相电有效电压double数组，按顺序从1-n相，单位V，多相电设备才有 |
| cs | array | 多相电有效电流double数组，按顺序从1-n相，单位A，多相电设备才有 |
| ps | array | 多相电有效功率double数组，按顺序从1-n相，单位W，多相电设备才有 |
\*\*应答示例\*\*
```
{
&#x20; "method": "getEMRealtime",
&#x20; "result": "ok",
&#x20; "data": \[
&#x20;   {
&#x20;     "v": 237.1000061,
&#x20;     "vs": \[237.3490143,236.4700165,237.4820099],
&#x20;     "c": 0.091,
&#x20;     "cs": \[0.046,0.019,0.026],
&#x20;     "p": 4.263,
&#x20;     "ps": \[1.784,1.064,1.4150001],
&#x20;     "e":0
&#x20;   }
&#x20; ],
&#x20; "imei": "1745396239780",
&#x20; "frameId": "1745396239780",
&#x20; "timestamp": 1745396759
}
```
\*\*备注\*\*
无
\## 获取校准参数（getCalParams）
\*\*简要描述\*\*
获取电量计校准参数。
\*\*命令参数\*\*
| 参数名 | 必须 | 类型 | 说明 |
| --- | --- | --- | --- |
| method | 是 | string | getCalParams |
| frameId | 否 | string | 帧ID |
\*\*命令示例\*\*
```
{
&#x20; "method": "getCalParams",
&#x20; "frameId": "1745396239780"
}
```
\*\*应答参数说明\*\*
| 参数名 | 类型 | 说明 |
| --- | --- | --- |
| method | string | getCalParams |
| result | string | 返回结果。`ok`-成功；其他-具体失败原因 |
| calParams | object | 校准参数对象 |
| imei | string | 设备imei |
| frameId | string | 同命令`frameId` |
| timestamp | int | 秒级时间戳，WiFi款不支持 |
\- `calParams`校准参数对象说明
| 参数名 | 类型 | 说明 |
| --- | --- | --- |
| RL | double | 校准电阻值 |
\*\*应答示例\*\*
```
{
&#x20; "method": "getCalParams",
&#x20; "result": "ok",
  "calParams": {
    "RL": 0.24
  },
&#x20; "imei": "1745396239780",
&#x20; "frameId": "1745396239780",
&#x20; "timestamp": 1745396759
}
```
\*\*备注\*\*
无
\## 设置校准参数（setCalParams）
\*\*简要描述\*\*
设置电量计校准参数。
\*\*命令参数\*\*
| 参数名 | 必须 | 类型 | 说明 |
| --- | --- | --- | --- |
| method | 是 | string | setCalParams |
| calParams | 是 | object | 校准参数对象 |
| frameId | 否 | string | 帧ID |
\- `calParams`校准参数对象说明
| 参数名 | 必须 | 类型 | 说明 |
| --- | --- | --- | --- |
| RL | 是 | double | 校准电阻值 |
\*\*命令示例\*\*
```
{
&#x20; "method": "setCalParams",
  "calParams": {
    "RL": 0.24
  },
&#x20; "frameId": "1745396239780"
}
```
\*\*应答参数说明\*\*
| 参数名 | 类型 | 说明 |
| --- | --- | --- |
| method | string | setCalParams |
| result | string | 返回结果。`ok`-成功；其他-具体失败原因 |
| calParams | object | 校准参数对象 |
| imei | string | 设备imei |
| frameId | string | 同命令`frameId` |
| timestamp | int | 秒级时间戳，WiFi款不支持 |
\- `calParams`校准参数对象说明
| 参数名 | 类型 | 说明 |
| --- | --- | --- |
| RL | double | 校准电阻值 |
\*\*应答示例\*\*
```
{
&#x20; "method": "setCalParams",
&#x20; "result": "ok",
  "calParams": {
    "RL": 0.24
  },
&#x20; "imei": "1745396239780",
&#x20; "frameId": "1745396239780",
&#x20; "timestamp": 1745396759
}
```
\*\*备注\*\*
无
\## 重置校准参数（resetCalParams）
\*\*简要描述\*\*
将电量计校准参数重置为默认值，不同类型设备有不同的默认值。
\*\*命令参数\*\*
| 参数名 | 必须 | 类型 | 说明 |
| --- | --- | --- | --- |
| method | 是 | string | resetCalParams |
| frameId | 否 | string | 帧ID |
\*\*命令示例\*\*
```
{
&#x20; "method": "resetCalParams",
&#x20; "frameId": "1745396239780"
}
```
\*\*应答参数说明\*\*
| 参数名 | 类型 | 说明 |
| --- | --- | --- |
| method | string | resetCalParams |
| result | string | 返回结果。`ok`-成功；其他-具体失败原因 |
| calParams | object | 校准参数对象 |
| imei | string | 设备imei |
| frameId | string | 同命令`frameId` |
| timestamp | int | 秒级时间戳，WiFi款不支持 |
\- `calParams`校准参数对象说明
| 参数名 | 类型 | 说明 |
| --- | --- | --- |
| RL | double | 校准电阻值 |
\*\*应答示例\*\*
```
{
&#x20; "method": "resetCalParams",
&#x20; "result": "ok",
  "calParams": {
    "RL": 0.24
  },
&#x20; "imei": "1745396239780",
&#x20; "frameId": "1745396239780",
&#x20; "timestamp": 1745396759
}
```
\*\*备注\*\*
无
\## 自动校准参数（autoCal）
\*\*简要描述\*\*
自动校准电量计校准参数。
注意：调用此命令前，需要开启稳定功率的负载，power值需填入负载的功率（例如：校准负载为3500W，则填入3500W）。
\*\*命令参数\*\*
| 参数名 | 必须 | 类型 | 说明 |
| --- | --- | --- | --- |
| method | 是 | string | autoCal |
| power | 是 | double | 自动校准的负载功率 |
| frameId | 否 | string | 帧ID |
\*\*命令示例\*\*
```
{
&#x20; "method": "autoCal",
&#x20; "power": 500,
&#x20; "frameId": "1745396239780"
}
```
\*\*应答参数说明\*\*
| 参数名 | 类型 | 说明 |
| --- | --- | --- |
| method | string | autoCal |
| result | string | 返回结果。`ok`-成功；其他-具体失败原因 |
| calParams | object | 校准参数对象 |
| imei | string | 设备imei |
| frameId | string | 同命令`frameId` |
| timestamp | int | 秒级时间戳，WiFi款不支持 |
\- `calParams`校准参数对象说明
| 参数名 | 类型 | 说明 |
| --- | --- | --- |
| RL | double | 校准电阻值 |
\*\*应答示例\*\*
```
{
&#x20; "method": "autoCal",
&#x20; "result": "ok",
  "calParams": {
    "RL": 0.24
  },
&#x20; "imei": "1745396239780",
&#x20; "frameId": "1745396239780",
&#x20; "timestamp": 1745396759
}
```
\*\*备注\*\*
无
\[TOC]
| 4G喇叭 | 4G开关 | WiFi喇叭 | WiFi开关 |
| --- | --- | --- | --- |
| <font color="red">\*\*\&times;\*\*</font> | <font color="green">\*\*\&radic;\*\*</font> | <font color="red">\*\*\&times;\*\*</font> | <font color="red">\*\*\&times;\*\*</font> |
支持此功能的设备类型：<font color="green">\*\*\&radic;\*\*</font>-支持 <font color="red">\*\*\&times;\*\*</font>-不支持
\## 定时任务说明
定时任务，分为：
\- 普通定时任务，如周一、三、五，13:00执行打开开关动作。
\- 循环定时任务，如周一、三、五，11:30 - 15:10，循环执行开5分钟，关1分钟循环动作。
每个插槽/开关可以有多组定时任务。
\## 获取所有定时任务（getTimeTasks）
\*\*简要描述\*\*
获取所有插槽定时任务。
\*\*命令参数\*\*
| 参数名 | 必须 | 类型 | 说明 |
| --- | --- | --- | --- |
| method | 是 | string | getTimeTasks |
| frameId | 否 | string | 帧ID |
\*\*命令示例\*\*
```
{
&#x20; "method": "getTimeTasks",
&#x20; "frameId": "1745396239780"
}
```
\*\*应答参数说明\*\*
| 参数名 | 类型 | 说明 |
| --- | --- | --- |
| method | string | getTimeTasks |
| result | string | 返回结果。`ok`-成功；其他-具体失败原因 |
| tasks | array | 定时任务对象数组。按顺序从插槽/开关1到插槽/开关n |
| imei | string | 设备imei |
| frameId | string | 同命令`frameId` |
| timestamp | int | 秒级时间戳，WiFi款不支持 |
\- `tasks`数组对象说明
| 参数名 | 类型 | 说明 |
| --- | --- | --- |
| loopTimeTasks | array | 循环定时任务对象数组 |
| timeTasks | array | 普通定时任务对象数组 |
\- `loopTimeTasks`循环定时数组对象说明
| 参数名 | 类型 | 说明 |
| --- | --- | --- |
| id | string | 任务id，由设置定时任务时分配 |
| enable | bool | 任务是否启用。`true`-启用，`false`-不启用 |
| weekDays | array | 每周的星期几数组，数组值为1-7，对应星期一到星期天。空数组则表示仅一次，处理完后`enable`会变为`false` |
| sHour | int | 每天循环开始的小时 |
| sMinute | int | 每天循环开始的分钟 |
| eHour | int | 每天循环结束的小时 |
| eMinute | int | 每天循环结束的分钟 |
| onMins | int | 循环中打开的分钟数 |
| offMins | int | 循环中关闭的分钟数 |
\- `timeTasks`普通定时数组对象说明
| 参数名 | 类型 | 说明 |
| --- | --- | --- |
| id | string | 任务id，由设置定时任务时分配 |
| enable | bool | 任务是否启用。`true`-启用，`false`-不启用 |
| weekDays | array | 每周的星期几数组，数组值为1-7，对应星期一到星期天。空数组则表示仅一次，处理完后`enable`会变为`false` |
| hour | int | 每天动作小时 |
| minute | int | 每天动作分钟 |
| action | string | 动作。`on`-打开；`off`-关闭；`toggle`-翻转 |
| uploadEnable | bool | 任务触发时是否上报（v5.0.1版本及以上才支持）。`true`-上报，`false`-不上报 |
\*\*应答示例\*\*
```
{
&#x20; "tasks":
&#x20; \[
&#x20;   {
&#x20;     "loopTimeTasks":
&#x20;     \[
&#x20;       {
&#x20;         "id": "1702288670498",
&#x20;         "sHour": 8,
&#x20;         "sMinute": 0,
&#x20;         "enable": true,
&#x20;         "offMins": 10,
&#x20;         "eMinute": 0,
&#x20;         "onMins": 5,
&#x20;         "weekDays": \[1,4,5],
&#x20;         "eHour": 10
&#x20;       }
&#x20;     ],
&#x20;     "timeTasks":
&#x20;     \[
&#x20;       {
&#x20;         "minute": 30,
&#x20;         "hour": 10,
&#x20;         "id": "1702288645833",
&#x20;         "enable": true,
&#x20;         "weekDays": \[1,2,3,7],
&#x20;         "action": "on",
&#x20;         "uploadEnable": false
&#x20;       },
&#x20;       {
&#x20;         "minute": 10,
&#x20;         "hour": 8,
&#x20;         "id": "1702288818397",
&#x20;         "enable": false,
&#x20;         "weekDays": \[1,2,6],
&#x20;         "action": "toggle",
&#x20;         "uploadEnable": false
&#x20;       }
&#x20;     ]
&#x20;   }
  ],
&#x20; "timestamp": 1702288823,
&#x20; "imei": "861959069365794",
&#x20; "frameId": "2147483647",
&#x20; "result": "ok",
&#x20; "method": "getTimeTasks"
}
```
\*\*备注\*\*
无
\## 设置所有定时任务（setTimeTasks）
\*\*简要描述\*\*
设置所有插槽/开关定时任务。
\*\*命令参数\*\*
| 参数名 | 必须 | 类型 | 说明 |
| --- | --- | --- | --- |
| method | 是 | string | setTimeTasks |
| tasks | array | 定时任务对象数组。按顺序从插槽/开关1到插槽/开关n |
| frameId | 否 | string | 帧ID |
\- `tasks`数组对象说明
| 参数名 | 必须 | 类型 | 说明 |
| --- | --- | --- |
| loopTimeTasks | 否 | array | 循环定时任务对象数组 |
| timeTasks | 否 | array | 普通定时任务对象数组 |
\- `loopTimeTasks`循环定时数组对象说明
| 参数名 | 必须 | 类型 | 说明 |
| --- | --- | --- |
| id | 是 | string | 任务id，由设置定时任务时分配 |
| enable | 是 | bool | 任务是否启用。`true`-启用，`false`-不启用 |
| weekDays | 是 | array | 每周的星期几数组，数组值为1-7，对应星期一到星期天。空数组则表示仅一次，处理完后`enable`会变为`false` |
| sHour | 是 | int | 每天循环开始的小时 |
| sMinute | 是 | int | 每天循环开始的分钟 |
| eHour | 是 | int | 每天循环结束的小时 |
| eMinute | 是 | int | 每天循环结束的分钟 |
| onMins | 是 | int | 循环中打开的分钟数 |
| offMins | 是 | int | 循环中关闭的分钟数 |
\- `timeTasks`普通定时数组对象说明
| 参数名 | 必须 | 类型 | 说明 |
| --- | --- | --- |
| id | 是 | string | 任务id，由设置定时任务时分配 |
| enable | 是 | bool | 任务是否启用。`true`-启用，`false`-不启用 |
| weekDays | 是 | array | 每周的星期几数组，数组值为1-7，对应星期一到星期天。空数组则表示仅一次，处理完后`enable`会变为false |
| hour | 是 | int | 每天动作小时 |
| minute | 是 | int | 每天动作分钟 |
| action | 是 | string | 动作。`on`-打开；`off`-关闭；`toggle`-翻转 |
| uploadEnable | bool | 任务触发时是否上报（v5.0.1版本及以上才支持）。`true`-上报，`false`-不上报 |
\*\*命令示例\*\*
```
{
&#x20; "tasks":
&#x20; \[
&#x20;   {
&#x20;     "loopTimeTasks":
&#x20;     \[
&#x20;       {
&#x20;         "id": "1702288670498",
&#x20;         "sHour": 8,
&#x20;         "sMinute": 0,
&#x20;         "enable": true,
&#x20;         "offMins": 10,
&#x20;         "eMinute": 0,
&#x20;         "onMins": 5,
&#x20;         "weekDays": \[1,4,5],
&#x20;         "eHour": 10
&#x20;       }
&#x20;     ],
&#x20;     "timeTasks":
&#x20;     \[
&#x20;       {
&#x20;         "minute": 30,
&#x20;         "hour": 10,
&#x20;         "id": "1702288645833",
&#x20;         "enable": true,
&#x20;         "weekDays": \[1,2,3,7],
&#x20;         "action": "on",
&#x20;         "uploadEnable": false
&#x20;       },
&#x20;       {
&#x20;         "minute": 10,
&#x20;         "hour": 8,
&#x20;         "id": "1702288818397",
&#x20;         "enable": false,
&#x20;         "weekDays": \[1,2,6],
&#x20;         "action": "toggle",
&#x20;         "uploadEnable": false
&#x20;       }
&#x20;     ]
&#x20;   }
  ],
&#x20; "frameId": "2147483647",
&#x20; "method": "setTimeTasks"
}
```
\*\*应答参数说明\*\*
| 参数名 | 类型 | 说明 |
| --- | --- | --- |
| method | string | getTimeTasks |
| result | string | 返回结果。`ok`-成功；其他-具体失败原因 |
| tasks | array | 定时任务对象数组。按顺序从插槽/开关1到插槽/开关n |
| imei | string | 设备imei |
| frameId | string | 同命令`frameId` |
| timestamp | int | 秒级时间戳，WiFi款不支持 |
\- `tasks`数组对象说明
| 参数名 | 类型 | 说明 |
| --- | --- | --- |
| loopTimeTasks | array | 循环定时任务对象数组 |
| timeTasks | array | 普通定时任务对象数组 |
\- `loopTimeTasks`循环定时数组对象说明
| 参数名 | 类型 | 说明 |
| --- | --- | --- |
| id | string | 任务id，由设置定时任务时分配 |
| enable | bool | 任务是否启用。`true`-启用，`false`-不启用 |
| weekDays | array | 每周的星期几数组，数组值为1-7，对应星期一到星期天。空数组则表示仅一次，处理完后`enable`会变为`false` |
| sHour | int | 每天循环开始的小时 |
| sMinute | int | 每天循环开始的分钟 |
| eHour | int | 每天循环结束的小时 |
| eMinute | int | 每天循环结束的分钟 |
| onMins | int | 循环中打开的分钟数 |
| offMins | int | 循环中关闭的分钟数 |
\- `timeTasks`普通定时数组对象说明
| 参数名 | 类型 | 说明 |
| --- | --- | --- |
| id | string | 任务id，由设置定时任务时分配 |
| enable | bool | 任务是否启用。`true`-启用，`false`-不启用 |
| weekDays | array | 每周的星期几数组，数组值为1-7，对应星期一到星期天。空数组则表示仅一次，处理完后`enable`会变为`false` |
| hour | int | 每天动作小时 |
| minute | int | 每天动作分钟 |
| action | string | 动作。`on`-打开；`off`-关闭；`toggle`-翻转 |
| uploadEnable | bool | 任务触发时是否上报（v5.0.1版本及以上才支持）。`true`-上报，`false`-不上报 |
\*\*应答示例\*\*
```
{
&#x20; "tasks":
&#x20; \[
&#x20;   {
&#x20;     "loopTimeTasks":
&#x20;     \[
&#x20;       {
&#x20;         "id": "1702288670498",
&#x20;         "sHour": 8,
&#x20;         "sMinute": 0,
&#x20;         "enable": true,
&#x20;         "offMins": 10,
&#x20;         "eMinute": 0,
&#x20;         "onMins": 5,
&#x20;         "weekDays": \[1,4,5],
&#x20;         "eHour": 10
&#x20;       }
&#x20;     ],
&#x20;     "timeTasks":
&#x20;     \[
&#x20;       {
&#x20;         "minute": 30,
&#x20;         "hour": 10,
&#x20;         "id": "1702288645833",
&#x20;         "enable": true,
&#x20;         "weekDays": \[1,2,3,7],
&#x20;         "action": "on",
&#x20;         "uploadEnable": false
&#x20;       },
&#x20;       {
&#x20;         "minute": 10,
&#x20;         "hour": 8,
&#x20;         "id": "1702288818397",
&#x20;         "enable": false,
&#x20;         "weekDays": \[1,2,6],
&#x20;         "action": "toggle",
&#x20;         "uploadEnable": false
&#x20;       }
&#x20;     ]
&#x20;   }
  ],
&#x20; "timestamp": 1702288823,
&#x20; "imei": "861959069365794",
&#x20; "frameId": "2147483647",
&#x20; "result": "ok",
&#x20; "method": "getTimeTasks"
}
```
\*\*备注\*\*
无
\## 获取单个插槽/开关定时任务（getSlotTimeTasks）
\*\*简要描述\*\*
获取单个插槽/开关定时任务
\*\*命令参数\*\*
| 参数名 | 必须 | 类型 | 说明 |
| --- | --- | --- | --- |
| method | 是 | string | getSlotTimeTasks |
| frameId | 否 | string | 帧ID |
\*\*命令示例\*\*
```
{
&#x20; "method": "getSlotTimeTasks",
&#x20; "frameId": "1745396239780"
}
```
\*\*应答参数说明\*\*
| 参数名 | 类型 | 说明 |
| --- | --- | --- |
| method | string | getSlotTimeTasks |
| result | string | 返回结果。`ok`-成功；其他-具体失败原因 |
| loopTimeTasks | array | 循环定时任务对象数组 |
| timeTasks | array | 普通定时任务对象数组 |
| imei | string | 设备imei |
| frameId | string | 同命令`frameId` |
| timestamp | int | 秒级时间戳，WiFi款不支持 |
\- `loopTimeTasks`循环定时数组对象说明
| 参数名 | 类型 | 说明 |
| --- | --- | --- |
| id | string | 任务id，由设置定时任务时分配 |
| enable | bool | 任务是否启用。`true`-启用，`false`-不启用 |
| weekDays | array | 每周的星期几数组，数组值为1-7，对应星期一到星期天。空数组则表示仅一次，处理完后`enable`会变为`false` |
| sHour | int | 每天循环开始的小时 |
| sMinute | int | 每天循环开始的分钟 |
| eHour | int | 每天循环结束的小时 |
| eMinute | int | 每天循环结束的分钟 |
| onMins | int | 循环中打开的分钟数 |
| offMins | int | 循环中关闭的分钟数 |
\- `timeTasks`普通定时数组对象说明
| 参数名 | 类型 | 说明 |
| --- | --- | --- |
| id | string | 任务id，由设置定时任务时分配 |
| enable | bool | 任务是否启用。`true`-启用，`false`-不启用 |
| weekDays | array | 每周的星期几数组，数组值为1-7，对应星期一到星期天。空数组则表示仅一次，处理完后`enable`会变为`false` |
| hour | int | 每天动作小时 |
| minute | int | 每天动作分钟 |
| action | string | 动作。`on`-打开；`off`-关闭；`toggle`-翻转 |
| uploadEnable | bool | 任务触发时是否上报（v5.0.1版本及以上才支持）。`true`-上报，`false`-不上报 |
\*\*应答示例\*\*
```
{
&#x20; "loopTimeTasks": \[
&#x20;   {
&#x20;     "id": "1702288670498",
&#x20;     "sHour": 8,
&#x20;     "sMinute": 0,
&#x20;     "enable": true,
&#x20;     "offMins": 10,
&#x20;     "eMinute": 0,
&#x20;     "onMins": 5,
&#x20;     "weekDays": \[1,4,5],
&#x20;     "eHour": 10
&#x20;   }
&#x20; ],
&#x20; "timeTasks": \[
&#x20;   {
&#x20;     "minute": 30,
&#x20;     "hour": 10,
&#x20;     "id": "1702288645833",
&#x20;     "enable": true,
&#x20;     "weekDays": \[1,2,3,7],
&#x20;     "action": "on",
&#x20;     "uploadEnable": false
&#x20;   },
&#x20;   {
&#x20;     "minute": 10,
&#x20;     "hour": 8,
&#x20;     "id": "1702288818397",
&#x20;     "enable": false,
&#x20;     "weekDays": \[1,2,6],
&#x20;     "action": "toggle",
&#x20;     "uploadEnable": false
&#x20;   }
&#x20; ],
&#x20; "timestamp": 1702288823,
&#x20; "imei": "861959069365794",
&#x20; "frameId": "2147483647",
&#x20; "result": "ok",
&#x20; "method": "getSlotTimeTasks"
}
```
\*\*备注\*\*
无
\## 设置单个插槽/开关定时任务（setSlotTimeTasks）
\*\*简要描述\*\*
设置单个插槽/开关定时任务。
\*\*命令参数\*\*
| 参数名 | 必须 | 类型 | 说明 |
| --- | --- | --- | --- |
| method | 是 | string | setSlotTimeTasks |
| loopTimeTasks | 否 | array | 循环定时任务对象数组 |
| timeTasks | 否 | array | 普通定时任务对象数组 |
| frameId | 否 | string | 帧ID |
\- `loopTimeTasks`循环定时数组对象说明
| 参数名 | 必须 | 类型 | 说明 |
| --- | --- | --- |
| id | 是 | string | 任务id，由设置定时任务时分配 |
| enable | 是 | bool | 任务是否启用。`true`-启用，`false`-不启用 |
| weekDays | 是 | array | 每周的星期几数组，数组值为1-7，对应星期一到星期天。空数组则表示仅一次，处理完后`enable`会变为`false` |
| sHour | 是 | int | 每天循环开始的小时 |
| sMinute | 是 | int | 每天循环开始的分钟 |
| eHour | 是 | int | 每天循环结束的小时 |
| eMinute | 是 | int | 每天循环结束的分钟 |
| onMins | 是 | int | 循环中打开的分钟数 |
| offMins | 是 | int | 循环中关闭的分钟数 |
\- `timeTasks`普通定时数组对象说明
| 参数名 | 必须 | 类型 | 说明 |
| --- | --- | --- |
| id | 是 | string | 任务id，由设置定时任务时分配 |
| enable | 是 | bool | 任务是否启用。`true`-启用，`false`-不启用 |
| weekDays | 是 | array | 每周的星期几数组，数组值为1-7，对应星期一到星期天。空数组则表示仅一次，处理完后`enable`会变为`false` |
| hour | 是 | int | 每天动作小时 |
| minute | 是 | int | 每天动作分钟 |
| action | 是 | string | 动作。`on`-打开；`off`-关闭；`toggle`-翻转 |
| uploadEnable | bool | 任务触发时是否上报（v5.0.1版本及以上才支持）。`true`-上报，`false`-不上报 |
\*\*命令示例\*\*
```
{
&#x20; "method": "setSlotTimeTasks",
&#x20; "slotNum": 1,
&#x20; "timeTasks": \[
&#x20;   {
&#x20;     "id": "1702288645833",
&#x20;     "weekDays": \[1,2,3,7],
&#x20;     "hour": 10,
&#x20;     "minute": 30,
&#x20;     "action": "on",
&#x20;     "enable": true,
&#x20;     "uploadEnable": false
&#x20;   }
&#x20; ],
&#x20; "loopTimeTasks": \[
&#x20;   {
&#x20;     "id": "1702288670498",
&#x20;     "weekDays": \[1,4,5],
&#x20;     "sHour": 8,
&#x20;     "sMinute": 0,
&#x20;     "eHour": 10,
&#x20;     "eMinute": 0,
&#x20;     "onMins": 5,
&#x20;     "offMins": 10,
&#x20;     "enable": true
&#x20;   }
&#x20; ],
&#x20; "frameId": "1702288672404"
}
```
\*\*应答参数说明\*\*
| 参数名 | 类型 | 说明 |
| --- | --- | --- |
| method | string | setSlotTimeTasks |
| result | string | 返回结果。`ok`-成功；其他-具体失败原因 |
| loopTimeTasks | array | 循环定时任务对象数组 |
| timeTasks | array | 普通定时任务对象数组 |
| imei | string | 设备imei |
| frameId | string | 同命令`frameId` |
| timestamp | int | 秒级时间戳，WiFi款不支持 |
\- `loopTimeTasks`循环定时数组对象说明
| 参数名 | 类型 | 说明 |
| --- | --- | --- |
| id | string | 任务id，由设置定时任务时分配 |
| enable | bool | 任务是否启用。`true`-启用，`false`-不启用 |
| weekDays | array | 每周的星期几数组，数组值为1-7，对应星期一到星期天。空数组则表示仅一次，处理完后`enable`会变为`false` |
| sHour | int | 每天循环开始的小时 |
| sMinute | int | 每天循环开始的分钟 |
| eHour | int | 每天循环结束的小时 |
| eMinute | int | 每天循环结束的分钟 |
| onMins | int | 循环中打开的分钟数 |
| offMins | int | 循环中关闭的分钟数 |
\- `timeTasks`普通定时数组对象说明
| 参数名 | 类型 | 说明 |
| --- | --- | --- |
| id | string | 任务id，由设置定时任务时分配 |
| enable | bool | 任务是否启用。`true`-启用，`false`-不启用 |
| weekDays | array | 每周的星期几数组，数组值为1-7，对应星期一到星期天。空数组则表示仅一次，处理完后`enable`会变为`false` |
| hour | int | 每天动作小时 |
| minute | int | 每天动作分钟 |
| action | string | 动作。`on`-打开；`off`-关闭；`toggle`-翻转 |
| uploadEnable | bool | 任务触发时是否上报（v5.0.1版本及以上才支持）。`true`-上报，`false`-不上报 |
\*\*应答示例\*\*
```
{
&#x20; "loopTimeTasks": \[
&#x20;   {
&#x20;     "id": "1702288670498",
&#x20;     "sHour": 8,
&#x20;     "sMinute": 0,
&#x20;     "enable": true,
&#x20;     "offMins": 10,
&#x20;     "eMinute": 0,
&#x20;     "onMins": 5,
&#x20;     "weekDays": \[1,4,5],
&#x20;     "eHour": 10
&#x20;   }
&#x20; ],
&#x20; "timeTasks": \[
&#x20;   {
&#x20;     "minute": 30,
&#x20;     "hour": 10,
&#x20;     "id": "1702288645833",
&#x20;     "enable": true,
&#x20;     "weekDays": \[1,2,3,7],
&#x20;     "action": "on",
&#x20;     "uploadEnable": false
&#x20;   },
&#x20;   {
&#x20;     "minute": 10,
&#x20;     "hour": 8,
&#x20;     "id": "1702288818397",
&#x20;     "enable": false,
&#x20;     "weekDays": \[1,2,6],
&#x20;     "action": "toggle",
&#x20;     "uploadEnable": false
&#x20;   }
&#x20; ],
&#x20; "timestamp": 1702288823,
&#x20; "imei": "861959069365794",
&#x20; "frameId": "2147483647",
&#x20; "result": "ok",
&#x20; "method": "setSlotTimeTasks"
}
```
\*\*备注\*\*
无
\## 上报定时任务事件（timeEvent）
\*\*简要描述\*\*
上报定时任务事件。
\*\*命令参数\*\*
无
\*\*命令示例\*\*
无
\*\*应答参数说明\*\*
| 参数名 | 类型 | 说明 |
| --- | --- | --- |
| method | string | timeEvent |
| taskIndex | int | 任务索引，从1开始 |
| slotNum | int | 插槽编号 |
| slots | array | 插槽状态int数组，按顺序从插槽1到插槽n，子项值：`0`-关闭;`1`-打开 |
| task | object | 触发的定时任务 |
| imei | string | 设备imei |
| timestamp | int | 秒级时间戳，WiFi款不支持 |
\- `task`触发的普通定时对象说明
| 参数名 | 类型 | 说明 |
| --- | --- | --- |
| id | string | 任务id，由设置定时任务时分配 |
| enable | bool | 任务是否启用。`true`-启用，`false`-不启用 |
| weekDays | array | 每周的星期几数组，数组值为1-7，对应星期一到星期天。空数组则表示仅一次，处理完后`enable`会变为`false` |
| hour | int | 每天动作小时 |
| minute | int | 每天动作分钟 |
| action | string | 动作。`on`-打开；`off`-关闭；`toggle`-翻转 |
| uploadEnable | bool | 任务触发时是否上报（v5.0.1版本及以上才支持）。`true`-上报，`false`-不上报 |
\*\*应答示例\*\*
```
{
&#x20;   "taskIndex": 1,
&#x20;   "timestamp": 1779346021,
&#x20;   "task": {
&#x20;       "minute": 47,
&#x20;       "enable": true,
&#x20;       "uploadEnable": true,
&#x20;       "id": "1779345917718",
&#x20;       "weekDays": \[
&#x20;           1,
&#x20;           4,
&#x20;           5
&#x20;       ],
&#x20;       "action": "toggle",
&#x20;       "hour": 14
&#x20;   },
&#x20;   "slots": \[
&#x20;       1
&#x20;   ],
&#x20;   "imei": "863434084747622",
&#x20;   "slotNum": 1,
&#x20;   "method": "timeEvent"
}
```
\*\*备注\*\*
无
\## 获取电量计统计信息（getEMStatistics）
\*\*简要描述\*\*
获取电量计统计信息。
\*\*命令参数\*\*
| 参数名 | 必须 | 类型 | 说明 |
| --- | --- | --- | --- |
| method | 是 | string | getEMStatistics |
| q | 否 | string | 查询字符串，不传或`all`-全部统计信息；`month`-月统计信息；`day`-日统计信息；`hour`-小时统计信息；`hourSum`-小时累加统计信息；`total`-总电量信息。可组合使用，如：`total,day,hour` 表示返回总度数、日统计信息、小时统计信息 |
| frameId | 否 | string | 帧ID |
\*\*命令示例\*\*
```
{
&#x20; "method": "getEMStatistics",
&#x20; "q": "all",
&#x20; "frameId": "1745396239780"
}
```
\*\*应答参数说明\*\*
| 参数名 | 类型 | 说明 |
| --- | --- | --- |
| method | string | getEMStatistics |
| result | string | 返回结果。`ok`-成功；其他-具体失败原因 |
| data | array | 插槽电量计统计信息对象数组。按顺序从插槽1\~插槽n。数值单位kWh（度） |
| imei | string | 设备imei |
| frameId | string | 同命令`frameId` |
| timestamp | int | 秒级时间戳，WiFi款不支持 |
\- `data`数组对象说明
| 参数名 | 类型 | 说明 |
| --- | --- | --- |
| total | double | 累计电量 |
| hourSumData | array | 半小时累计电量数组，隔天累加。数组长度48，从0时0分（包含）\~0时30分（不包含），0时30分（包含）\~1时0分（不包含），…依此类推。跨天的电量是累加的，比如 昨天的00:00 - 00:30 和 今天的00:00 - 00:30 的电量是累加在一起的。新订单启动会清空累计电量。 |
| hourData | array | 半小时累计电量数组。注意：半小时累计电量数组数据可能不连续，请按照具体日期值为date的键值。具体例子请参考下面应答示例。半小时累计电量数组只保留最近48个记录，没有记录到的日期表示此半小时无累计电量或超出最长记录 |
| dayData | array | 日累计电量数组。注意：日累计电量数组数据可能不连续，请按照具体日期值为date的键值。具体例子请参考下面应答示例。日累计电量数组只保留最近30个记录，没有记录到的日期表示当天无累计电量或超出最长记录 |
| monthData | array | 月累计电量数组。注意：月累计电量数组数据可能不连续，请按照具体日期值为date的键值。具体例子请参考下面应答示例。月累计电量数组只保留最近12个记录，没有记录到的日期表示当月无累计电量或超出最长记录 |
\- `hourData`数组对象说明
| 参数名 | 类型 | 说明 |
| --- | --- | --- |
| date | string | 日期，格式 yyyyMMddHHmm，mm值为00或30 |
| kwh | double | 累计电量 |
\- `dayData`数组对象说明
| 参数名 | 类型 | 说明 |
| --- | --- | --- |
| date | string | 日期，格式 yyyyMMdd |
| kwh | double | 累计电量 |
\- `monthData`数组对象说明
| 参数名 | 类型 | 说明 |
| --- | --- | --- |
| date | string | 日期，格式 yyyyMM |
| kwh | double | 累计电量 |
\*\*应答示例\*\*
&#x20;```
{
&#x20; "timestamp": 1712472461,
&#x20; "imei": "861959069365794",
&#x20; "data": \[
&#x20;   {
&#x20;     "total": 8.401,
&#x20;     "dayData": \[
&#x20;       {"date": "20240407","kwh": 5.5258},
&#x20;       {"date": "20240330","kwh": 0.5}
&#x20;     ],
&#x20;     "hourData": \[
&#x20;       {"date": "202404071030","kwh": 5.5258},
&#x20;       {"date": "202404071000","kwh": 0.5}
&#x20;     ],
&#x20;     "hourSumData":\[0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,2.267,1.233,0,0,0,0,0.541,0,0,0,0,0,0,0,0,0.158,0,0,0,0,0,0,0,0,0],
&#x20;     "monthData": \[
&#x20;       {"date": "202404","kwh": 5.5258},
&#x20;       {"date": "202403","kwh": 0.5}
&#x20;     ]
&#x20;   }
&#x20; ],
&#x20; "result": "ok",
&#x20; "frameId": 2147483647,
&#x20; "method": "getEMStatistics"
}
```
\*\*备注\*\*
无
\## 清空电量计统计信息（clearEMStatistics）
\*\*简要描述\*\*
清空电量计统计信息。
\*\*命令参数\*\*
| 参数名 | 必须 | 类型 | 说明 |
| --- | --- | --- | --- |
| method | 是 | string | clearEMStatistics |
| slotNum | 否 | int | 要清空电量统计信息的插槽编号。不传或`0`表示清空所有插槽的电量统计信息 |
| frameId | 否 | string | 帧ID |
\*\*命令示例\*\*
```
{
&#x20; "method": "clearEMStatistics",
&#x20; "slotNum": 1,
&#x20; "frameId": "1745396239780"
}
```
\*\*应答参数说明\*\*
| 参数名 | 类型 | 说明 |
| --- | --- | --- |
| method | string | clearEMStatistics |
| result | string | 返回结果。`ok`-成功；其他-具体失败原因 |
| imei | string | 设备imei |
| frameId | string | 同命令`frameId` |
| timestamp | int | 秒级时间戳，WiFi款不支持 |
\*\*应答示例\*\*
```
{
&#x20; "method": "clearEMStatistics",
&#x20; "result": "ok",
&#x20; "imei": "1745396239780",
&#x20; "frameId": "1745396239780",
&#x20; "timestamp": 1745396759
}
```
\*\*备注\*\*
无
\## 获取日志（getLogs）
\*\*简要描述\*\*
获取设备日志，最多100条，超过100条最新的会覆盖最旧的，日志为临时数据，设备重启会清空日志。
\*\*命令参数\*\*
| 参数名 | 必须 | 类型 | 说明 |
| --- | --- | --- | --- |
| method | 是 | string | getLogs |
| num | 否 | int | 要获取的最近日志条数，如10表示获取最近10条日志，不传表示获取所有 |
| frameId | 否 | string | 帧ID |
\*\*命令示例\*\*
```
{
&#x20; "method": "getLogs",
&#x20; "num": 10,
&#x20; "frameId": "1745396239780"
}
```
\*\*应答参数说明\*\*
| 参数名 | 类型 | 说明 |
| --- | --- | --- |
| method | string | getLogs |
| result | string | 返回结果。`ok`-成功；其他-具体失败原因 |
| logs | array | 日志数据对象数组 |
| imei | string | 设备imei |
| frameId | string | 同命令`frameId` |
| timestamp | int | 秒级时间戳，WiFi款不支持 |
\- `logs`日志数组对象说明
| 参数名 | 类型 | 说明 |
| --- | --- | --- |
| type | string | 日志类型，`action`-action命令；`delayTask`-延时任务；`timeTask`-定时任务；`loopTimeTask`-循环定时任务；`keyEvent`-物理按键动作 |
| act | string | 动作，`on`-打开；`off`-关闭；`toggle`-翻转 |
| state | string | 动作后状态。`0`-关闭；`1`-打开 |
| sNum | int | 插槽编号 |
| t | int | 日志发生时间戳（秒级） |
\*\*应答示例\*\*
```
{
&#x20; "logs": \[
&#x20;   {
&#x20;     "act": "on",
&#x20;     "t": 1712461200,
&#x20;     "state": 1,
&#x20;     "sNum": 1,
&#x20;     "type": "loopTimeTask"
&#x20;   },
&#x20;   {
&#x20;     "act": "off",
&#x20;     "t": 1712461140,
&#x20;     "state": 0,
&#x20;     "sNum": 1,
&#x20;     "type": "loopTimeTask"
&#x20;   },
&#x20;   {
&#x20;     "act": "toggle",
&#x20;     "t": 1712461100,
&#x20;     "state": 1,
&#x20;     "sNum": 1,
&#x20;     "type": "action"
&#x20;   },
&#x20;   {
&#x20;     "act": "off",
&#x20;     "t": 1712461098,
&#x20;     "state": 0,
&#x20;     "sNum": 1,
&#x20;     "type": "action"
&#x20;   },
&#x20;   {
&#x20;     "act": "on",
&#x20;     "t": 1712461097,
&#x20;     "state": 1,
&#x20;     "sNum": 1,
&#x20;     "type": "action"
&#x20;   }
&#x20; ],
&#x20; "timestamp": 1712461448,
&#x20; "imei": "1745396239780",
&#x20; "frameId": "1745396239780",
&#x20; "result": "ok",
&#x20; "method": "getLogs"
}
```
\*\*备注\*\*
无
\## 发送RS48命令（send485）（测试中）
\*\*简要描述\*\*
发送RS48命令。
\*\*命令参数\*\*
| 参数名 | 必须 | 类型 | 说明 |
| --- | --- | --- | --- |
| method | 是 | string | send485 |
| baudRate | 否 | int | 串口波特率，`2400`\~`2000000`，默认`115200`，为空则使用之前配置波特率 |
| sendWaitMs | int | RS485自动上报多个命令间隔毫秒数，默认`300` |
| dataArray | 是 | array | RS485十六进制命令字符串数组 |
| frameId | 否 | string | 帧ID |
\*\*命令示例\*\*
```
{
&#x20; "method": "send485",
&#x20; "baudRate": 115200,
&#x20; "sendWaitMs": 300,
&#x20; "dataArray": \["343830303133","234345287283"],
&#x20; "frameId": "1745396239780"
}
```
\*\*应答参数说明\*\*
| 参数名 | 类型 | 说明 |
| --- | --- | --- |
| method | string | send485 |
| result | string | 返回结果。`ok`-成功；其他-具体失败原因 |
| imei | string | 设备imei |
| frameId | string | 同命令`frameId` |
| timestamp | int | 秒级时间戳，WiFi款不支持 |
\*\*应答示例\*\*
```
{
&#x20; "method": "send485",
&#x20; "result": "ok",
&#x20; "imei": "1745396239780",
&#x20; "frameId": "1745396239780",
&#x20; "timestamp": 1745396759
}
```
\*\*备注\*\*
无
\## 接收RS48数据上传事件（recv485）（测试中）
\*\*简要描述\*\*
接收RS48数据上传事件。
\*\*命令参数\*\*
\*\*命令示例\*\*
\*\*应答参数说明\*\*
| 参数名 | 类型 | 说明 |
| --- | --- | --- |
| method | string | recv485 |
| result | string | 返回结果。`ok`-成功；其他-具体失败原因 |
| data | string | RS485接收到的十六进制字符串 |
| num | int | 对应多个命令的编号，从`1`开始 |
| frameId | string | 同命令`frameId`，自动上报的此值为空 |
| imei | string | 设备imei |
| timestamp | int | 秒级时间戳，WiFi款不支持 |
\*\*应答示例\*\*
```
{
&#x20; "method": "recv485",
&#x20; "result": "ok",
&#x20; "data": "343830303133",
&#x20; "imei": "1745396239780",
&#x20; "num": 1,
&#x20; "frameId": "1745396239780",
&#x20; "timestamp": 1745396759
}
```
\*\*备注\*\*
无
\[TOC]
| 4G喇叭 | 4G开关 | WiFi喇叭 | WiFi开关 |
| --- | --- | --- | --- |
| <font color="green">\*\*\&radic;\*\*</font> | <font color="green">\*\*\&radic;\*\*</font> | <font color="red">\*\*\&times;\*\*</font> | <font color="red">\*\*\&times;\*\*</font> |
支持此功能的设备类型：<font color="green">\*\*\&radic;\*\*</font>-支持 <font color="red">\*\*\&times;\*\*</font>-不支持
\## 设置时间（setTime）
\*\*简要描述\*\*
设置设备时间。
\*\*命令参数\*\*
| 参数名 | 必须 | 类型 | 说明 |
| --- | --- | --- | --- |
| method | 是 | string | setTime |
| timestamp | 是 | int | 秒级时间戳 |
| frameId | 否 | string | 帧ID |
\*\*命令示例\*\*
```
{
&#x20; "method": "setTime",
&#x20; "timestamp": 1745456483,
&#x20; "frameId": "1745456483900"
}
```
\*\*应答参数说明\*\*
| 参数名 | 类型 | 说明 |
| --- | --- | --- |
| method | string | setTime |
| result | string | 返回结果。`ok`-成功；其他-具体失败原因 |
| imei | string | 设备imei |
| frameId | string | 同命令`frameId` |
| timestamp | int | 秒级时间戳 |
\*\*应答示例\*\*
```
{
&#x20; "method": "getSimCheck",
&#x20; "result": "ok",
&#x20; "timestamp": 1745456483,
&#x20; "imei": "864536072949900",
&#x20; "frameId": "1745456483900"
}
```
\*\*备注\*\*
无
\## 获取开机物联卡预警信息（getSimCheck）
\*\*简要描述\*\*
获取开机物联卡预警信息。
如启用，开机后会播报“物联卡xxxxxx状态：xxx，物联卡流量剩余xxMB，物联卡剩余xx天到期”。
\*\*命令参数\*\*
| 参数名 | 必须 | 类型 | 说明 |
| --- | --- | --- | --- |
| method | 是 | string | getSimCheck |
| frameId | 否 | string | 帧ID |
\*\*命令示例\*\*
```
{
&#x20; "method": "getSimCheck",
&#x20; "frameId": "1745396239780"
}
```
\*\*应答参数说明\*\*
| 参数名 | 类型 | 说明 |
| --- | --- | --- |
| method | string | getSimCheck |
| result | string | 返回结果。`ok`-成功；其他-具体失败原因 |
| enabled | bool | `true`-启动，`false`-不启动 |
| leftDays | int | `0`-播报剩余天数；大于0则在剩余天数内播报 |
| dataBalance | int | `0`-播报剩余流量；大于0则在剩余流量内播报（单位MB） |
| imei | string | 设备imei |
| frameId | string | 同命令`frameId` |
| timestamp | int | 秒级时间戳，WiFi款不支持 |
\*\*应答示例\*\*
```
{
&#x20; "method": "getSimCheck",
&#x20; "result": "ok",
&#x20; "enabled": true,
&#x20; "leftDays": 0,
&#x20; "dataBalance": 0,
&#x20; "imei": "1745396239780",
&#x20; "frameId": "1745396239780",
&#x20; "timestamp": 1745396759
}
```
\*\*备注\*\*
无
\## 设置开机物联卡预警信息（setSimCheck）
\*\*简要描述\*\*
设置开机物联卡预警信息。
\*\*命令参数\*\*
| 参数名 | 必须 | 类型 | 说明 |
| --- | --- | --- | --- |
| method | 是 | string | setSimCheck |
| enabled | 是 | bool | `true`-启动，`false`-不启动 |
| leftDays | 是 | int | `0`-播报剩余天数；大于0则在剩余天数内播报 |
| dataBalance | 是 | int | `0`-播报剩余流量；大于0则在剩余流量内播报（单位MB） |
| frameId | 否 | string | 帧ID |
\*\*命令示例\*\*
```
{
&#x20; "method": "setSimCheck",
&#x20; "enabled": true,
&#x20; "leftDays": 0,
&#x20; "dataBalance": 0,
&#x20; "frameId": "1745456483900"
}
```
\*\*应答参数说明\*\*
| 参数名 | 类型 | 说明 |
| --- | --- | --- |
| method | string | setSimCheck |
| result | string | 返回结果。`ok`-成功；其他-具体失败原因 |
| enabled | bool | `true`-启动，`false`-不启动 |
| imei | string | 设备imei |
| leftDays | int | `0`-播报剩余天数；大于0则在剩余天数内播报 |
| dataBalance | int | `0`-播报剩余流量；大于0则在剩余流量内播报（单位MB） |
| frameId | string | 同命令`frameId` |
| timestamp | int | 秒级时间戳 |
\*\*应答示例\*\*
```
{
&#x20; "method": "setSimCheck",
&#x20; "result": "ok",
&#x20; "enabled": true,
&#x20; "leftDays": 0,
&#x20; "dataBalance": 0,
&#x20; "timestamp": 1745456483,
&#x20; "imei": "864536072949900",
&#x20; "frameId": "1745456483900"
}
```
\*\*备注\*\*
无
\## 物联卡预警（simCheck）
\*\*简要描述\*\*
根据设置的参数进行物联卡预警检查，如启动会进行播报。
\*\*命令参数\*\*
| 参数名 | 必须 | 类型 | 说明 |
| --- | --- | --- | --- |
| method | 是 | string | simCheck |
| frameId | 否 | string | 帧ID |
\*\*命令示例\*\*
```
{
&#x20; "method": "simCheck",
&#x20; "frameId": "1745456483900"
}
```
\*\*应答参数说明\*\*
| 参数名 | 类型 | 说明 |
| --- | --- | --- |
| method | string | simCheck |
| result | string | 返回结果。`ok`-成功；其他-具体失败原因 |
| imei | string | 设备imei |
| frameId | string | 同命令`frameId` |
| timestamp | int | 秒级时间戳 |
\*\*应答示例\*\*
```
{
&#x20; "method": "simCheck",
&#x20; "result": "ok",
&#x20; "imei": "864536072949900",
&#x20; "timestamp": 1745456483,
&#x20; "frameId": "1745456483900"
}
```
\*\*备注\*\*
无
