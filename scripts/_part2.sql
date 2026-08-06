
-- ============================================================================================
-- 阶段 1：键重映射（务必整体在一个事务里执行；核对阶段 3 的结果后再 COMMIT）
--
-- 每条语句的统一范式（null-aware，对应约定 1）：
--   CASE WHEN 规范键存在 AND 规范键值 <> JSON null
--        THEN 保留规范键的值（规范键是修复后的前端写入的，视为权威）
--        ELSE 用旧键的值 rescue 上去（规范键缺失、或其值为 null 时）
--   END，最后统一 JSON_REMOVE 掉旧键。
-- 注：JSON_EXTRACT 在路径不存在时返回 SQL NULL，JSON_TYPE(SQL NULL) 也是 SQL NULL，
--     故 "<> 'NULL'" 在键缺失时求值为 NULL（假），会正确落到 ELSE 分支。
-- ============================================================================================

-- ⚠️ 执行下面任何 UPDATE 之前，先做库内备份表（把 YYYYMMDD 换成当天日期，并取消注释）：
-- CREATE TABLE protocol_configs_backup_YYYYMMDD AS SELECT * FROM protocol_configs;

START TRANSACTION;

-- --------------------------------------------------------------------------------------------
-- 1.1 MQTT / 安圣 MQTT
--     host→Host, port→Port, endpoint→EndpointUrl, username→Username, password→Password,
--     clientIdPrefix→ClientIdPrefix, cleanSession→CleanSession, qosLevel→QosLevel
--     说明：EndpointUrl 不是 MqttProtocolOptions 的成员，归一只为统一键形态；
--           System.Text.Json 会忽略未知成员，不会抛异常（与 C# 实现的已知取舍一致）。
-- --------------------------------------------------------------------------------------------

-- host → Host
UPDATE protocol_configs
SET Config = JSON_REMOVE(
        CASE WHEN JSON_CONTAINS_PATH(Config, 'one', '$."Host"')
                  AND JSON_TYPE(JSON_EXTRACT(Config, '$."Host"')) <> 'NULL'
             THEN CAST(Config AS JSON)
             ELSE JSON_SET(CAST(Config AS JSON), '$."Host"', JSON_EXTRACT(Config, '$."host"'))
        END,
        '$."host"')
WHERE JSON_VALID(Config)
  AND JSON_TYPE(CAST(Config AS JSON)) = 'OBJECT'
  AND JSON_CONTAINS_PATH(Config, 'one', '$."host"')
  AND LOWER(REPLACE(REPLACE(REPLACE(Type, '_', ''), '-', ''), ' ', '')) IN ('mqtt', 'anshengmqtt');

-- port → Port
UPDATE protocol_configs
SET Config = JSON_REMOVE(
        CASE WHEN JSON_CONTAINS_PATH(Config, 'one', '$."Port"')
                  AND JSON_TYPE(JSON_EXTRACT(Config, '$."Port"')) <> 'NULL'
             THEN CAST(Config AS JSON)
             ELSE JSON_SET(CAST(Config AS JSON), '$."Port"', JSON_EXTRACT(Config, '$."port"'))
        END,
        '$."port"')
WHERE JSON_VALID(Config)
  AND JSON_TYPE(CAST(Config AS JSON)) = 'OBJECT'
  AND JSON_CONTAINS_PATH(Config, 'one', '$."port"')
  AND LOWER(REPLACE(REPLACE(REPLACE(Type, '_', ''), '-', ''), ' ', '')) IN ('mqtt', 'anshengmqtt');

-- endpoint → EndpointUrl
UPDATE protocol_configs
SET Config = JSON_REMOVE(
        CASE WHEN JSON_CONTAINS_PATH(Config, 'one', '$."EndpointUrl"')
                  AND JSON_TYPE(JSON_EXTRACT(Config, '$."EndpointUrl"')) <> 'NULL'
             THEN CAST(Config AS JSON)
             ELSE JSON_SET(CAST(Config AS JSON), '$."EndpointUrl"', JSON_EXTRACT(Config, '$."endpoint"'))
        END,
        '$."endpoint"')
WHERE JSON_VALID(Config)
  AND JSON_TYPE(CAST(Config AS JSON)) = 'OBJECT'
  AND JSON_CONTAINS_PATH(Config, 'one', '$."endpoint"')
  AND LOWER(REPLACE(REPLACE(REPLACE(Type, '_', ''), '-', ''), ' ', '')) IN ('mqtt', 'anshengmqtt');

-- username → Username
UPDATE protocol_configs
SET Config = JSON_REMOVE(
        CASE WHEN JSON_CONTAINS_PATH(Config, 'one', '$."Username"')
                  AND JSON_TYPE(JSON_EXTRACT(Config, '$."Username"')) <> 'NULL'
             THEN CAST(Config AS JSON)
             ELSE JSON_SET(CAST(Config AS JSON), '$."Username"', JSON_EXTRACT(Config, '$."username"'))
        END,
        '$."username"')
WHERE JSON_VALID(Config)
  AND JSON_TYPE(CAST(Config AS JSON)) = 'OBJECT'
  AND JSON_CONTAINS_PATH(Config, 'one', '$."username"')
  AND LOWER(REPLACE(REPLACE(REPLACE(Type, '_', ''), '-', ''), ' ', '')) IN ('mqtt', 'anshengmqtt');

-- password → Password
UPDATE protocol_configs
SET Config = JSON_REMOVE(
        CASE WHEN JSON_CONTAINS_PATH(Config, 'one', '$."Password"')
                  AND JSON_TYPE(JSON_EXTRACT(Config, '$."Password"')) <> 'NULL'
             THEN CAST(Config AS JSON)
             ELSE JSON_SET(CAST(Config AS JSON), '$."Password"', JSON_EXTRACT(Config, '$."password"'))
        END,
        '$."password"')
WHERE JSON_VALID(Config)
  AND JSON_TYPE(CAST(Config AS JSON)) = 'OBJECT'
  AND JSON_CONTAINS_PATH(Config, 'one', '$."password"')
  AND LOWER(REPLACE(REPLACE(REPLACE(Type, '_', ''), '-', ''), ' ', '')) IN ('mqtt', 'anshengmqtt');

-- clientIdPrefix / clientidprefix → ClientIdPrefix（COALESCE：camelCase 优先，其次全小写）
UPDATE protocol_configs
SET Config = JSON_REMOVE(
        CASE WHEN JSON_CONTAINS_PATH(Config, 'one', '$."ClientIdPrefix"')
                  AND JSON_TYPE(JSON_EXTRACT(Config, '$."ClientIdPrefix"')) <> 'NULL'
             THEN CAST(Config AS JSON)
             ELSE JSON_SET(CAST(Config AS JSON), '$."ClientIdPrefix"',
                    COALESCE(JSON_EXTRACT(Config, '$."clientIdPrefix"'),
                             JSON_EXTRACT(Config, '$."clientidprefix"')))
        END,
        '$."clientIdPrefix"', '$."clientidprefix"')
WHERE JSON_VALID(Config)
  AND JSON_TYPE(CAST(Config AS JSON)) = 'OBJECT'
  AND JSON_CONTAINS_PATH(Config, 'one', '$."clientIdPrefix"', '$."clientidprefix"')
  AND LOWER(REPLACE(REPLACE(REPLACE(Type, '_', ''), '-', ''), ' ', '')) IN ('mqtt', 'anshengmqtt');

-- cleanSession / cleansession → CleanSession
UPDATE protocol_configs
SET Config = JSON_REMOVE(
        CASE WHEN JSON_CONTAINS_PATH(Config, 'one', '$."CleanSession"')
                  AND JSON_TYPE(JSON_EXTRACT(Config, '$."CleanSession"')) <> 'NULL'
             THEN CAST(Config AS JSON)
             ELSE JSON_SET(CAST(Config AS JSON), '$."CleanSession"',
                    COALESCE(JSON_EXTRACT(Config, '$."cleanSession"'),
                             JSON_EXTRACT(Config, '$."cleansession"')))
        END,
        '$."cleanSession"', '$."cleansession"')
WHERE JSON_VALID(Config)
  AND JSON_TYPE(CAST(Config AS JSON)) = 'OBJECT'
  AND JSON_CONTAINS_PATH(Config, 'one', '$."cleanSession"', '$."cleansession"')
  AND LOWER(REPLACE(REPLACE(REPLACE(Type, '_', ''), '-', ''), ' ', '')) IN ('mqtt', 'anshengmqtt');

-- qosLevel / qoslevel → QosLevel
UPDATE protocol_configs
SET Config = JSON_REMOVE(
        CASE WHEN JSON_CONTAINS_PATH(Config, 'one', '$."QosLevel"')
                  AND JSON_TYPE(JSON_EXTRACT(Config, '$."QosLevel"')) <> 'NULL'
             THEN CAST(Config AS JSON)
             ELSE JSON_SET(CAST(Config AS JSON), '$."QosLevel"',
                    COALESCE(JSON_EXTRACT(Config, '$."qosLevel"'),
                             JSON_EXTRACT(Config, '$."qoslevel"')))
        END,
        '$."qosLevel"', '$."qoslevel"')
WHERE JSON_VALID(Config)
  AND JSON_TYPE(CAST(Config AS JSON)) = 'OBJECT'
  AND JSON_CONTAINS_PATH(Config, 'one', '$."qosLevel"', '$."qoslevel"')
  AND LOWER(REPLACE(REPLACE(REPLACE(Type, '_', ''), '-', ''), ' ', '')) IN ('mqtt', 'anshengmqtt');

-- --------------------------------------------------------------------------------------------
-- 1.2 Modbus TCP：host→Host, port→Port
--     Type 只写了 'modbus' 的历史行按 TCP 处理（只涉及 host/port；若该行其实是 RTU，
--     它的 serialPort 等键会作为未知键原样保留，不会被改坏）。
-- --------------------------------------------------------------------------------------------

-- host → Host
UPDATE protocol_configs
SET Config = JSON_REMOVE(
        CASE WHEN JSON_CONTAINS_PATH(Config, 'one', '$."Host"')
                  AND JSON_TYPE(JSON_EXTRACT(Config, '$."Host"')) <> 'NULL'
             THEN CAST(Config AS JSON)
             ELSE JSON_SET(CAST(Config AS JSON), '$."Host"', JSON_EXTRACT(Config, '$."host"'))
        END,
        '$."host"')
WHERE JSON_VALID(Config)
  AND JSON_TYPE(CAST(Config AS JSON)) = 'OBJECT'
  AND JSON_CONTAINS_PATH(Config, 'one', '$."host"')
  AND LOWER(REPLACE(REPLACE(REPLACE(Type, '_', ''), '-', ''), ' ', '')) IN ('modbustcp', 'modbus');

-- port → Port
UPDATE protocol_configs
SET Config = JSON_REMOVE(
        CASE WHEN JSON_CONTAINS_PATH(Config, 'one', '$."Port"')
                  AND JSON_TYPE(JSON_EXTRACT(Config, '$."Port"')) <> 'NULL'
             THEN CAST(Config AS JSON)
             ELSE JSON_SET(CAST(Config AS JSON), '$."Port"', JSON_EXTRACT(Config, '$."port"'))
        END,
        '$."port"')
WHERE JSON_VALID(Config)
  AND JSON_TYPE(CAST(Config AS JSON)) = 'OBJECT'
  AND JSON_CONTAINS_PATH(Config, 'one', '$."port"')
  AND LOWER(REPLACE(REPLACE(REPLACE(Type, '_', ''), '-', ''), ' ', '')) IN ('modbustcp', 'modbus');

-- --------------------------------------------------------------------------------------------
-- 1.3 Modbus RTU：serialPort→PortName, baudRate→BaudRate
-- --------------------------------------------------------------------------------------------

-- serialPort / serialport → PortName
UPDATE protocol_configs
SET Config = JSON_REMOVE(
        CASE WHEN JSON_CONTAINS_PATH(Config, 'one', '$."PortName"')
                  AND JSON_TYPE(JSON_EXTRACT(Config, '$."PortName"')) <> 'NULL'
             THEN CAST(Config AS JSON)
             ELSE JSON_SET(CAST(Config AS JSON), '$."PortName"',
                    COALESCE(JSON_EXTRACT(Config, '$."serialPort"'),
                             JSON_EXTRACT(Config, '$."serialport"')))
        END,
        '$."serialPort"', '$."serialport"')
WHERE JSON_VALID(Config)
  AND JSON_TYPE(CAST(Config AS JSON)) = 'OBJECT'
  AND JSON_CONTAINS_PATH(Config, 'one', '$."serialPort"', '$."serialport"')
  AND LOWER(REPLACE(REPLACE(REPLACE(Type, '_', ''), '-', ''), ' ', '')) = 'modbusrtu';

-- baudRate / baudrate → BaudRate
UPDATE protocol_configs
SET Config = JSON_REMOVE(
        CASE WHEN JSON_CONTAINS_PATH(Config, 'one', '$."BaudRate"')
                  AND JSON_TYPE(JSON_EXTRACT(Config, '$."BaudRate"')) <> 'NULL'
             THEN CAST(Config AS JSON)
             ELSE JSON_SET(CAST(Config AS JSON), '$."BaudRate"',
                    COALESCE(JSON_EXTRACT(Config, '$."baudRate"'),
                             JSON_EXTRACT(Config, '$."baudrate"')))
        END,
        '$."baudRate"', '$."baudrate"')
WHERE JSON_VALID(Config)
  AND JSON_TYPE(CAST(Config AS JSON)) = 'OBJECT'
  AND JSON_CONTAINS_PATH(Config, 'one', '$."baudRate"', '$."baudrate"')
  AND LOWER(REPLACE(REPLACE(REPLACE(Type, '_', ''), '-', ''), ' ', '')) = 'modbusrtu';

-- --------------------------------------------------------------------------------------------
-- 1.4 OPC UA：endpoint→EndpointUrl
-- --------------------------------------------------------------------------------------------

UPDATE protocol_configs
SET Config = JSON_REMOVE(
        CASE WHEN JSON_CONTAINS_PATH(Config, 'one', '$."EndpointUrl"')
                  AND JSON_TYPE(JSON_EXTRACT(Config, '$."EndpointUrl"')) <> 'NULL'
             THEN CAST(Config AS JSON)
             ELSE JSON_SET(CAST(Config AS JSON), '$."EndpointUrl"', JSON_EXTRACT(Config, '$."endpoint"'))
        END,
        '$."endpoint"')
WHERE JSON_VALID(Config)
  AND JSON_TYPE(CAST(Config AS JSON)) = 'OBJECT'
  AND JSON_CONTAINS_PATH(Config, 'one', '$."endpoint"')
  AND LOWER(REPLACE(REPLACE(REPLACE(Type, '_', ''), '-', ''), ' ', '')) = 'opcua';
