-- ============================================================================================
-- normalize_protocol_config_keys.sql
--
-- ⚠️⚠️ 本脚本【未在任何数据库上执行过】，包括开发库。提交时它只是一份待评审的文本。 ⚠️⚠️
--     必须经 DBA / 运维评审，并在备份就绪后由人工执行。研发侧不碰任何数据库。
--
-- 执行前必须完成的两件事（缺一不可）：
--   1) 物理备份：
--        mysqldump -u<user> -p <db> protocol_configs > protocol_configs_YYYYMMDD.sql
--   2) 库内快照表（把 YYYYMMDD 换成当天日期，取消注释后执行）：
--        CREATE TABLE protocol_configs_backup_YYYYMMDD AS SELECT * FROM protocol_configs;
--
--     回滚（COMMIT 之后才发现问题时用快照表还原）：
--        UPDATE protocol_configs p
--          JOIN protocol_configs_backup_YYYYMMDD b ON b.Id = p.Id
--           SET p.Config = b.Config;
--
-- --------------------------------------------------------------------------------------------
-- 用途
--   把 protocol_configs.Config 里的存量小写 / 字符串型键，归一为 PascalCase + 正确类型，
--   与后端 Data/ProtocolConfigNormalizer.cs、前端 normalizeLegacyConfigKeys 保持【逐条一致】。
--   三处规则若要改，必须三处一起改，否则同一份数据在不同入口会得到不同结果。
--
-- 与运行时兜底的关系
--   Infrastructure/Protocol/Adapters/ProtocolJsonOptions.CaseInsensitive 现已带
--   JsonNumberHandling.AllowReadingFromString，运行时能容忍 "502" 这种字符串端口。
--   但那只是【兜底】，不是数据清洗的替代品：
--     - 兜底救不了 {"Port":null}（显式 null 覆盖属性初始化器，数值属性直接抛 JsonException）；
--     - 兜底救不了 {"host":"x"} 这类小写键在严格选项下的丢失；
--     - 脏数据留在库里，任何新写的、没带兜底选项的反序列化点都会重新踩坑。
--   所以仍然要做这次数据归一。
--
-- --------------------------------------------------------------------------------------------
-- 重要行为约定（与 ProtocolConfigNormalizer.cs 逐条对齐）
--
--   规则 0：值为 JSON null 的目标键【一律删除】（不限于数值属性，字符串属性同样删）。
--   规则 1：旧键改名到规范键时，只有【已存在且值非 null】的规范键才算权威；
--           规范键值为 null 时视同不存在，让旧键的真实值 rescue 上去。
--           {"host":"1.2.3.4","Host":null} → {"Host":"1.2.3.4"}
--   规则 2：同一目标键被多个旧键命中（serialPort / serialport 并存）→ 先出现者胜。
--   规则 3：数值属性做「字符串整数 → JSON 数字」矫正，仅限 TRIM 后完全匹配 ^[+-]?[0-9]+$ 的值；
--           "COM3" / "5000.5" / "1e3" 这类不猜测，原样保留，留到阶段 3 由人工确认。
--           ⚠️ 正号必须放行：C# 用 long.TryParse(..., NumberStyles.Integer, ...)，而
--              NumberStyles.Integer = AllowLeadingWhite | AllowTrailingWhite | AllowLeadingSign，
--              所以 "+502" 在 C# 侧【会】被矫正成 502。SQL 若写成 '^-?[0-9]+$' 就会漏掉它，
--              造成同一份数据后端归一成数字、SQL 迁移后仍是字符串的三处分叉。
--              已由测试 NumericProperty_IntegerParsingBoundary_IsLockedForCrossStackParity 锁定。
--   规则 4：空串删键【只对数值属性生效】。空串对 string Host 是合法值，不能一起删。
--   规则 5：未识别的键原样保留，不删不改。
--
-- 规则 0 的依据
--   System.Text.Json 遇到显式 null 会【覆盖】C# 属性初始化器：
--     public string Host { get; set; } = "localhost";
--     Deserialize<ModbusTcpOptions>("{\"Host\":null}").Host  ==  null   ← 不是 "localhost"
--   即留着 null 比删掉键更坏。已由后端测试
--   ExplicitJsonNull_OverridesPropertyInitializerDefault_NotIgnored 实测锁定。
--
-- 已知限制
--   MySQL 的 JSON 路径【大小写敏感】，做不到 C# 那样一次覆盖任意大小写写法。
--   因此脚本对每个键显式枚举实际出现过的两种写法（camelCase + 全小写），
--   例如 '$."clientIdPrefix"' 与 '$."clientidprefix"'。若阶段 3 校验查出别的写法
--   （如 CLIENTIDPREFIX），需补语句后重跑，不要手改数据。
--
-- 幂等性
--   全部语句的 WHERE 都以「旧键存在」或「值仍是字符串 / null」为前提，
--   已归一的行不会被二次命中。整脚本可重复执行，结果稳定。
--
-- 环境
--   MySQL 5.7.8+ / 8.0（依赖 JSON_VALID / JSON_TYPE / JSON_CONTAINS_PATH / JSON_SET / JSON_REMOVE）。
--   表名假定为 protocol_configs，列为 Id / Type / Config；与实际库不符时请整体替换。
-- ============================================================================================


-- ============================================================================================
-- 阶段 0：只读预检（不改数据，先看清楚要动多少行、有没有脏到无法自动处理的）
--
--   其中 0.5 需优先看：它列出的行在【本脚本执行前就已经连不上】，
--   应用侧的运行时兜底（AllowReadingFromString）救不动它们。
-- ============================================================================================

-- 0.1 非法 JSON 行：脚本【不处理】这些行，必须人工修，否则后续语句会静默跳过它们
SELECT Id, Type, Config
FROM protocol_configs
WHERE Config IS NOT NULL
  AND Config <> ''
  AND NOT JSON_VALID(Config);

-- 0.2 待迁移行数按协议分组统计
SELECT LOWER(REPLACE(REPLACE(REPLACE(Type, '_', ''), '-', ''), ' ', '')) AS NormalizedType,
       COUNT(*) AS RowCountToMigrate
FROM protocol_configs
WHERE JSON_VALID(Config)
  AND JSON_TYPE(CAST(Config AS JSON)) = 'OBJECT'
  AND JSON_CONTAINS_PATH(Config, 'one',
        '$."host"', '$."port"', '$."endpoint"', '$."username"', '$."password"',
        '$."clientidprefix"', '$."clientIdPrefix"', '$."cleansession"', '$."cleanSession"',
        '$."qoslevel"', '$."qosLevel"', '$."serialport"', '$."serialPort"',
        '$."baudrate"', '$."baudRate"')
GROUP BY NormalizedType
ORDER BY NormalizedType;

-- 0.3 数值属性当前仍是字符串的行（这些会在阶段 2 被矫正）
SELECT Id, Type, Config
FROM protocol_configs
WHERE JSON_VALID(Config)
  AND (JSON_TYPE(JSON_EXTRACT(Config, '$."port"'))     = 'STRING'
    OR JSON_TYPE(JSON_EXTRACT(Config, '$."Port"'))     = 'STRING'
    OR JSON_TYPE(JSON_EXTRACT(Config, '$."baudRate"')) = 'STRING'
    OR JSON_TYPE(JSON_EXTRACT(Config, '$."baudrate"')) = 'STRING'
    OR JSON_TYPE(JSON_EXTRACT(Config, '$."BaudRate"')) = 'STRING');

-- 0.4 含 JSON null 的行（阶段 1/2 会按规则 0 删键）。
--     RescueCandidate = 1 表示「规范键是 null 但同名旧键有真实值」，迁移后旧值会被救活。
SELECT Id, Type, Config,
       CASE WHEN (JSON_TYPE(JSON_EXTRACT(Config, '$."Host"')) = 'NULL'
                  AND JSON_CONTAINS_PATH(Config, 'one', '$."host"'))
              OR (JSON_TYPE(JSON_EXTRACT(Config, '$."Port"')) = 'NULL'
                  AND JSON_CONTAINS_PATH(Config, 'one', '$."port"'))
              OR (JSON_TYPE(JSON_EXTRACT(Config, '$."PortName"')) = 'NULL'
                  AND JSON_CONTAINS_PATH(Config, 'one', '$."serialPort"', '$."serialport"'))
              OR (JSON_TYPE(JSON_EXTRACT(Config, '$."EndpointUrl"')) = 'NULL'
                  AND JSON_CONTAINS_PATH(Config, 'one', '$."endpoint"'))
            THEN 1 ELSE 0 END AS RescueCandidate
FROM protocol_configs
WHERE JSON_VALID(Config)
  AND (JSON_TYPE(JSON_EXTRACT(Config, '$."Host"'))        = 'NULL'
    OR JSON_TYPE(JSON_EXTRACT(Config, '$."Port"'))        = 'NULL'
    OR JSON_TYPE(JSON_EXTRACT(Config, '$."PortName"'))    = 'NULL'
    OR JSON_TYPE(JSON_EXTRACT(Config, '$."BaudRate"'))    = 'NULL'
    OR JSON_TYPE(JSON_EXTRACT(Config, '$."EndpointUrl"')) = 'NULL'
    OR JSON_TYPE(JSON_EXTRACT(Config, '$."Username"'))    = 'NULL'
    OR JSON_TYPE(JSON_EXTRACT(Config, '$."Password"'))    = 'NULL');

-- 0.5 【高优先级】运行时兜底救不动、只能靠本脚本修的行 —— 这些行「此刻就是连不上的」。
--
--     背景：应用侧已启用 JsonNumberHandling.AllowReadingFromString（运行时兜底，见
--     Infrastructure/Protocol/Adapters/ProtocolJsonOptions.cs），它能把 "502" 这类字符串
--     数值救回来，因此容易让人以为「本脚本可以不执行」。这个推论是错的：
--     该兜底走 System.Text.Json 的数字解析，【不做 TRIM、不接受空串】。实测（.NET 8）：
--         "502" / "+502" / "0502" / "-1"  → 能绑定
--         " 502" / "502 " / "  502  "     → 抛 JsonException（连接失败）
--         ""                              → 抛 JsonException（连接失败）
--     而本脚本（以及 C# 归一化器、前端）三处都先 TRIM、且把空串按删键处理，
--     所以下面这批行【只有跑完本脚本才能恢复连接】。
--     对应的常驻回归用例：ProtocolConfigNormalizerTests
--         .RuntimeFallbackB_IsNotASupersetOfMigrationA_CoverageMatrix
--
--     注：MySQL 的 TRIM() 默认只裁剪半角空格，不裁剪制表符/换行；C# 的 Trim() 裁剪全部空白。
--     因此形如 "\t502" 的值本脚本不会矫正——但它会在阶段 3.2（仍为字符串）被拦下，
--     属于「暴露出来交人工」的安全失败，不会被静默改错。
SELECT Id, Type, Config, OffendingKey
FROM (
    SELECT Id, Type, Config,
           CASE
             WHEN JSON_TYPE(JSON_EXTRACT(Config, '$."Port"')) = 'STRING'
                  AND (JSON_UNQUOTE(JSON_EXTRACT(Config, '$."Port"')) <> TRIM(JSON_UNQUOTE(JSON_EXTRACT(Config, '$."Port"')))
                       OR TRIM(JSON_UNQUOTE(JSON_EXTRACT(Config, '$."Port"'))) = '') THEN 'Port'
             WHEN JSON_TYPE(JSON_EXTRACT(Config, '$."port"')) = 'STRING'
                  AND (JSON_UNQUOTE(JSON_EXTRACT(Config, '$."port"')) <> TRIM(JSON_UNQUOTE(JSON_EXTRACT(Config, '$."port"')))
                       OR TRIM(JSON_UNQUOTE(JSON_EXTRACT(Config, '$."port"'))) = '') THEN 'port'
             WHEN JSON_TYPE(JSON_EXTRACT(Config, '$."BaudRate"')) = 'STRING'
                  AND (JSON_UNQUOTE(JSON_EXTRACT(Config, '$."BaudRate"')) <> TRIM(JSON_UNQUOTE(JSON_EXTRACT(Config, '$."BaudRate"')))
                       OR TRIM(JSON_UNQUOTE(JSON_EXTRACT(Config, '$."BaudRate"'))) = '') THEN 'BaudRate'
             WHEN JSON_TYPE(JSON_EXTRACT(Config, '$."baudRate"')) = 'STRING'
                  AND (JSON_UNQUOTE(JSON_EXTRACT(Config, '$."baudRate"')) <> TRIM(JSON_UNQUOTE(JSON_EXTRACT(Config, '$."baudRate"')))
                       OR TRIM(JSON_UNQUOTE(JSON_EXTRACT(Config, '$."baudRate"'))) = '') THEN 'baudRate'
             WHEN JSON_TYPE(JSON_EXTRACT(Config, '$."baudrate"')) = 'STRING'
                  AND (JSON_UNQUOTE(JSON_EXTRACT(Config, '$."baudrate"')) <> TRIM(JSON_UNQUOTE(JSON_EXTRACT(Config, '$."baudrate"')))
                       OR TRIM(JSON_UNQUOTE(JSON_EXTRACT(Config, '$."baudrate"'))) = '') THEN 'baudrate'
             WHEN JSON_TYPE(JSON_EXTRACT(Config, '$."QosLevel"')) = 'STRING'
                  AND (JSON_UNQUOTE(JSON_EXTRACT(Config, '$."QosLevel"')) <> TRIM(JSON_UNQUOTE(JSON_EXTRACT(Config, '$."QosLevel"')))
                       OR TRIM(JSON_UNQUOTE(JSON_EXTRACT(Config, '$."QosLevel"'))) = '') THEN 'QosLevel'
             WHEN JSON_TYPE(JSON_EXTRACT(Config, '$."qosLevel"')) = 'STRING'
                  AND (JSON_UNQUOTE(JSON_EXTRACT(Config, '$."qosLevel"')) <> TRIM(JSON_UNQUOTE(JSON_EXTRACT(Config, '$."qosLevel"')))
                       OR TRIM(JSON_UNQUOTE(JSON_EXTRACT(Config, '$."qosLevel"'))) = '') THEN 'qosLevel'
             WHEN JSON_TYPE(JSON_EXTRACT(Config, '$."qoslevel"')) = 'STRING'
                  AND (JSON_UNQUOTE(JSON_EXTRACT(Config, '$."qoslevel"')) <> TRIM(JSON_UNQUOTE(JSON_EXTRACT(Config, '$."qoslevel"')))
                       OR TRIM(JSON_UNQUOTE(JSON_EXTRACT(Config, '$."qoslevel"'))) = '') THEN 'qoslevel'
             WHEN JSON_TYPE(JSON_EXTRACT(Config, '$."TimeoutMs"')) = 'STRING'
                  AND (JSON_UNQUOTE(JSON_EXTRACT(Config, '$."TimeoutMs"')) <> TRIM(JSON_UNQUOTE(JSON_EXTRACT(Config, '$."TimeoutMs"')))
                       OR TRIM(JSON_UNQUOTE(JSON_EXTRACT(Config, '$."TimeoutMs"'))) = '') THEN 'TimeoutMs'
             WHEN JSON_TYPE(JSON_EXTRACT(Config, '$."PollIntervalMs"')) = 'STRING'
                  AND (JSON_UNQUOTE(JSON_EXTRACT(Config, '$."PollIntervalMs"')) <> TRIM(JSON_UNQUOTE(JSON_EXTRACT(Config, '$."PollIntervalMs"')))
                       OR TRIM(JSON_UNQUOTE(JSON_EXTRACT(Config, '$."PollIntervalMs"'))) = '') THEN 'PollIntervalMs'
             WHEN JSON_TYPE(JSON_EXTRACT(Config, '$."DataBits"')) = 'STRING'
                  AND (JSON_UNQUOTE(JSON_EXTRACT(Config, '$."DataBits"')) <> TRIM(JSON_UNQUOTE(JSON_EXTRACT(Config, '$."DataBits"')))
                       OR TRIM(JSON_UNQUOTE(JSON_EXTRACT(Config, '$."DataBits"'))) = '') THEN 'DataBits'
             WHEN JSON_TYPE(JSON_EXTRACT(Config, '$."TimeoutSeconds"')) = 'STRING'
                  AND (JSON_UNQUOTE(JSON_EXTRACT(Config, '$."TimeoutSeconds"')) <> TRIM(JSON_UNQUOTE(JSON_EXTRACT(Config, '$."TimeoutSeconds"')))
                       OR TRIM(JSON_UNQUOTE(JSON_EXTRACT(Config, '$."TimeoutSeconds"'))) = '') THEN 'TimeoutSeconds'
             WHEN JSON_TYPE(JSON_EXTRACT(Config, '$."KeepAliveSeconds"')) = 'STRING'
                  AND (JSON_UNQUOTE(JSON_EXTRACT(Config, '$."KeepAliveSeconds"')) <> TRIM(JSON_UNQUOTE(JSON_EXTRACT(Config, '$."KeepAliveSeconds"')))
                       OR TRIM(JSON_UNQUOTE(JSON_EXTRACT(Config, '$."KeepAliveSeconds"'))) = '') THEN 'KeepAliveSeconds'
             WHEN JSON_TYPE(JSON_EXTRACT(Config, '$."CommandMinIntervalMs"')) = 'STRING'
                  AND (JSON_UNQUOTE(JSON_EXTRACT(Config, '$."CommandMinIntervalMs"')) <> TRIM(JSON_UNQUOTE(JSON_EXTRACT(Config, '$."CommandMinIntervalMs"')))
                       OR TRIM(JSON_UNQUOTE(JSON_EXTRACT(Config, '$."CommandMinIntervalMs"'))) = '') THEN 'CommandMinIntervalMs'
           END AS OffendingKey
    FROM protocol_configs
    WHERE JSON_VALID(Config)
      AND LOWER(REPLACE(REPLACE(REPLACE(Type, '_', ''), '-', ''), ' ', ''))
          IN ('mqtt', 'anshengmqtt', 'modbustcp', 'modbus', 'modbusrtu', 'opcua')
) AS t
WHERE OffendingKey IS NOT NULL;


-- ============================================================================================
-- 阶段 1：键重映射（务必整体在一个事务里执行；核对阶段 3 的结果后再决定 COMMIT）
--
-- 每条语句的统一范式（null-aware，对应规则 1）：
--   CASE WHEN 规范键存在 AND 规范键值 <> JSON null
--        THEN 保留规范键的值（视为权威）
--        ELSE 用旧键的值 rescue 上去（规范键缺失、或其值为 null 时）
--   END，最后统一 JSON_REMOVE 掉旧键。
--
-- 注：JSON_EXTRACT 在路径不存在时返回 SQL NULL，JSON_TYPE(SQL NULL) 也是 SQL NULL，
--     故 "<> 'NULL'" 在键缺失时求值为 NULL（假），会正确落到 ELSE 分支。
-- ============================================================================================

START TRANSACTION;

-- --------------------------------------------------------------------------------------------
-- 1.1 MQTT / 安圣 MQTT：host→Host、port→Port、endpoint→EndpointUrl、username→Username、
--     password→Password、clientIdPrefix→ClientIdPrefix、cleanSession→CleanSession、
--     qosLevel→QosLevel
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

-- clientIdPrefix / clientidprefix → ClientIdPrefix
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
-- 1.2 Modbus TCP：host→Host、port→Port
--     历史上只写 "modbus" 的行按 TCP 处理（与 C# Schemas["modbus"] = modbusTcp 一致）。
--     若该行其实是 RTU，其 serialPort 等键会作为未知键原样保留，不会被改坏。
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
-- 1.3 Modbus RTU：serialPort→PortName、baudRate→BaudRate
--     注意规范名是 PortName（不是 SerialPort），与 ModbusRtuOptions 一致。
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

-- ============================================================================================
-- 阶段 2：值矫正（键名此时已是 PascalCase，与协议无关，统一处理）
--
--   2.1 数值属性：字符串且 TRIM 后完全匹配 ^[+-]?[0-9]+$ → CAST 成整数（对应规则 3）
--       正号放行是刻意的，见头部规则 3 的说明；MySQL 的 CAST('+502' AS SIGNED) 同样得 502。
--   2.2 数值属性：空串 或 JSON null → 删除该键（对应规则 4 + 规则 0）
--   2.3 非数值属性：JSON null → 删除该键（对应规则 0，字符串属性同样删）
--
--   数值属性集合（与 C# ProtocolConfigNormalizer.NumericProperties 完全一致，共 9 个）：
--     Port / BaudRate / TimeoutMs / PollIntervalMs / QosLevel / DataBits /
--     TimeoutSeconds / KeepAliveSeconds / CommandMinIntervalMs
-- ============================================================================================

-- --------------------------------------------------------------------------------------------
-- 2.1 字符串整数 → JSON 数字
-- --------------------------------------------------------------------------------------------

UPDATE protocol_configs SET Config = JSON_SET(CAST(Config AS JSON), '$."Port"',
        CAST(TRIM(JSON_UNQUOTE(JSON_EXTRACT(Config, '$."Port"'))) AS SIGNED))
WHERE JSON_VALID(Config)
  AND LOWER(REPLACE(REPLACE(REPLACE(Type, '_', ''), '-', ''), ' ', ''))
      IN ('mqtt', 'anshengmqtt', 'modbustcp', 'modbus', 'modbusrtu', 'opcua')
  AND JSON_TYPE(JSON_EXTRACT(Config, '$."Port"')) = 'STRING'
  AND TRIM(JSON_UNQUOTE(JSON_EXTRACT(Config, '$."Port"'))) REGEXP '^[+-]?[0-9]+$';

UPDATE protocol_configs SET Config = JSON_SET(CAST(Config AS JSON), '$."BaudRate"',
        CAST(TRIM(JSON_UNQUOTE(JSON_EXTRACT(Config, '$."BaudRate"'))) AS SIGNED))
WHERE JSON_VALID(Config)
  AND LOWER(REPLACE(REPLACE(REPLACE(Type, '_', ''), '-', ''), ' ', ''))
      IN ('mqtt', 'anshengmqtt', 'modbustcp', 'modbus', 'modbusrtu', 'opcua')
  AND JSON_TYPE(JSON_EXTRACT(Config, '$."BaudRate"')) = 'STRING'
  AND TRIM(JSON_UNQUOTE(JSON_EXTRACT(Config, '$."BaudRate"'))) REGEXP '^[+-]?[0-9]+$';

UPDATE protocol_configs SET Config = JSON_SET(CAST(Config AS JSON), '$."TimeoutMs"',
        CAST(TRIM(JSON_UNQUOTE(JSON_EXTRACT(Config, '$."TimeoutMs"'))) AS SIGNED))
WHERE JSON_VALID(Config)
  AND LOWER(REPLACE(REPLACE(REPLACE(Type, '_', ''), '-', ''), ' ', ''))
      IN ('mqtt', 'anshengmqtt', 'modbustcp', 'modbus', 'modbusrtu', 'opcua')
  AND JSON_TYPE(JSON_EXTRACT(Config, '$."TimeoutMs"')) = 'STRING'
  AND TRIM(JSON_UNQUOTE(JSON_EXTRACT(Config, '$."TimeoutMs"'))) REGEXP '^[+-]?[0-9]+$';

UPDATE protocol_configs SET Config = JSON_SET(CAST(Config AS JSON), '$."PollIntervalMs"',
        CAST(TRIM(JSON_UNQUOTE(JSON_EXTRACT(Config, '$."PollIntervalMs"'))) AS SIGNED))
WHERE JSON_VALID(Config)
  AND LOWER(REPLACE(REPLACE(REPLACE(Type, '_', ''), '-', ''), ' ', ''))
      IN ('mqtt', 'anshengmqtt', 'modbustcp', 'modbus', 'modbusrtu', 'opcua')
  AND JSON_TYPE(JSON_EXTRACT(Config, '$."PollIntervalMs"')) = 'STRING'
  AND TRIM(JSON_UNQUOTE(JSON_EXTRACT(Config, '$."PollIntervalMs"'))) REGEXP '^[+-]?[0-9]+$';

UPDATE protocol_configs SET Config = JSON_SET(CAST(Config AS JSON), '$."QosLevel"',
        CAST(TRIM(JSON_UNQUOTE(JSON_EXTRACT(Config, '$."QosLevel"'))) AS SIGNED))
WHERE JSON_VALID(Config)
  AND LOWER(REPLACE(REPLACE(REPLACE(Type, '_', ''), '-', ''), ' ', ''))
      IN ('mqtt', 'anshengmqtt', 'modbustcp', 'modbus', 'modbusrtu', 'opcua')
  AND JSON_TYPE(JSON_EXTRACT(Config, '$."QosLevel"')) = 'STRING'
  AND TRIM(JSON_UNQUOTE(JSON_EXTRACT(Config, '$."QosLevel"'))) REGEXP '^[+-]?[0-9]+$';

UPDATE protocol_configs SET Config = JSON_SET(CAST(Config AS JSON), '$."DataBits"',
        CAST(TRIM(JSON_UNQUOTE(JSON_EXTRACT(Config, '$."DataBits"'))) AS SIGNED))
WHERE JSON_VALID(Config)
  AND LOWER(REPLACE(REPLACE(REPLACE(Type, '_', ''), '-', ''), ' ', ''))
      IN ('mqtt', 'anshengmqtt', 'modbustcp', 'modbus', 'modbusrtu', 'opcua')
  AND JSON_TYPE(JSON_EXTRACT(Config, '$."DataBits"')) = 'STRING'
  AND TRIM(JSON_UNQUOTE(JSON_EXTRACT(Config, '$."DataBits"'))) REGEXP '^[+-]?[0-9]+$';

UPDATE protocol_configs SET Config = JSON_SET(CAST(Config AS JSON), '$."TimeoutSeconds"',
        CAST(TRIM(JSON_UNQUOTE(JSON_EXTRACT(Config, '$."TimeoutSeconds"'))) AS SIGNED))
WHERE JSON_VALID(Config)
  AND LOWER(REPLACE(REPLACE(REPLACE(Type, '_', ''), '-', ''), ' ', ''))
      IN ('mqtt', 'anshengmqtt', 'modbustcp', 'modbus', 'modbusrtu', 'opcua')
  AND JSON_TYPE(JSON_EXTRACT(Config, '$."TimeoutSeconds"')) = 'STRING'
  AND TRIM(JSON_UNQUOTE(JSON_EXTRACT(Config, '$."TimeoutSeconds"'))) REGEXP '^[+-]?[0-9]+$';

UPDATE protocol_configs SET Config = JSON_SET(CAST(Config AS JSON), '$."KeepAliveSeconds"',
        CAST(TRIM(JSON_UNQUOTE(JSON_EXTRACT(Config, '$."KeepAliveSeconds"'))) AS SIGNED))
WHERE JSON_VALID(Config)
  AND LOWER(REPLACE(REPLACE(REPLACE(Type, '_', ''), '-', ''), ' ', ''))
      IN ('mqtt', 'anshengmqtt', 'modbustcp', 'modbus', 'modbusrtu', 'opcua')
  AND JSON_TYPE(JSON_EXTRACT(Config, '$."KeepAliveSeconds"')) = 'STRING'
  AND TRIM(JSON_UNQUOTE(JSON_EXTRACT(Config, '$."KeepAliveSeconds"'))) REGEXP '^[+-]?[0-9]+$';

UPDATE protocol_configs SET Config = JSON_SET(CAST(Config AS JSON), '$."CommandMinIntervalMs"',
        CAST(TRIM(JSON_UNQUOTE(JSON_EXTRACT(Config, '$."CommandMinIntervalMs"'))) AS SIGNED))
WHERE JSON_VALID(Config)
  AND LOWER(REPLACE(REPLACE(REPLACE(Type, '_', ''), '-', ''), ' ', ''))
      IN ('mqtt', 'anshengmqtt', 'modbustcp', 'modbus', 'modbusrtu', 'opcua')
  AND JSON_TYPE(JSON_EXTRACT(Config, '$."CommandMinIntervalMs"')) = 'STRING'
  AND TRIM(JSON_UNQUOTE(JSON_EXTRACT(Config, '$."CommandMinIntervalMs"'))) REGEXP '^[+-]?[0-9]+$';

-- --------------------------------------------------------------------------------------------
-- 2.2 数值属性的空串 / JSON null → 删键，让 DTO 默认值生效
--     （{"Port":""} 与 {"Port":null} 绑定到 int 都会抛 JsonException，兜底选项也救不了）
-- --------------------------------------------------------------------------------------------

UPDATE protocol_configs SET Config = JSON_REMOVE(CAST(Config AS JSON), '$."Port"')
WHERE JSON_VALID(Config)
  AND LOWER(REPLACE(REPLACE(REPLACE(Type, '_', ''), '-', ''), ' ', ''))
      IN ('mqtt', 'anshengmqtt', 'modbustcp', 'modbus', 'modbusrtu', 'opcua')
  AND (JSON_TYPE(JSON_EXTRACT(Config, '$."Port"')) = 'NULL'
  OR (JSON_TYPE(JSON_EXTRACT(Config, '$."Port"')) = 'STRING'
      AND TRIM(JSON_UNQUOTE(JSON_EXTRACT(Config, '$."Port"'))) = ''));

UPDATE protocol_configs SET Config = JSON_REMOVE(CAST(Config AS JSON), '$."BaudRate"')
WHERE JSON_VALID(Config)
  AND LOWER(REPLACE(REPLACE(REPLACE(Type, '_', ''), '-', ''), ' ', ''))
      IN ('mqtt', 'anshengmqtt', 'modbustcp', 'modbus', 'modbusrtu', 'opcua')
  AND (JSON_TYPE(JSON_EXTRACT(Config, '$."BaudRate"')) = 'NULL'
  OR (JSON_TYPE(JSON_EXTRACT(Config, '$."BaudRate"')) = 'STRING'
      AND TRIM(JSON_UNQUOTE(JSON_EXTRACT(Config, '$."BaudRate"'))) = ''));

UPDATE protocol_configs SET Config = JSON_REMOVE(CAST(Config AS JSON), '$."TimeoutMs"')
WHERE JSON_VALID(Config)
  AND LOWER(REPLACE(REPLACE(REPLACE(Type, '_', ''), '-', ''), ' ', ''))
      IN ('mqtt', 'anshengmqtt', 'modbustcp', 'modbus', 'modbusrtu', 'opcua')
  AND (JSON_TYPE(JSON_EXTRACT(Config, '$."TimeoutMs"')) = 'NULL'
  OR (JSON_TYPE(JSON_EXTRACT(Config, '$."TimeoutMs"')) = 'STRING'
      AND TRIM(JSON_UNQUOTE(JSON_EXTRACT(Config, '$."TimeoutMs"'))) = ''));

UPDATE protocol_configs SET Config = JSON_REMOVE(CAST(Config AS JSON), '$."PollIntervalMs"')
WHERE JSON_VALID(Config)
  AND LOWER(REPLACE(REPLACE(REPLACE(Type, '_', ''), '-', ''), ' ', ''))
      IN ('mqtt', 'anshengmqtt', 'modbustcp', 'modbus', 'modbusrtu', 'opcua')
  AND (JSON_TYPE(JSON_EXTRACT(Config, '$."PollIntervalMs"')) = 'NULL'
  OR (JSON_TYPE(JSON_EXTRACT(Config, '$."PollIntervalMs"')) = 'STRING'
      AND TRIM(JSON_UNQUOTE(JSON_EXTRACT(Config, '$."PollIntervalMs"'))) = ''));

UPDATE protocol_configs SET Config = JSON_REMOVE(CAST(Config AS JSON), '$."QosLevel"')
WHERE JSON_VALID(Config)
  AND LOWER(REPLACE(REPLACE(REPLACE(Type, '_', ''), '-', ''), ' ', ''))
      IN ('mqtt', 'anshengmqtt', 'modbustcp', 'modbus', 'modbusrtu', 'opcua')
  AND (JSON_TYPE(JSON_EXTRACT(Config, '$."QosLevel"')) = 'NULL'
  OR (JSON_TYPE(JSON_EXTRACT(Config, '$."QosLevel"')) = 'STRING'
      AND TRIM(JSON_UNQUOTE(JSON_EXTRACT(Config, '$."QosLevel"'))) = ''));

UPDATE protocol_configs SET Config = JSON_REMOVE(CAST(Config AS JSON), '$."DataBits"')
WHERE JSON_VALID(Config)
  AND LOWER(REPLACE(REPLACE(REPLACE(Type, '_', ''), '-', ''), ' ', ''))
      IN ('mqtt', 'anshengmqtt', 'modbustcp', 'modbus', 'modbusrtu', 'opcua')
  AND (JSON_TYPE(JSON_EXTRACT(Config, '$."DataBits"')) = 'NULL'
  OR (JSON_TYPE(JSON_EXTRACT(Config, '$."DataBits"')) = 'STRING'
      AND TRIM(JSON_UNQUOTE(JSON_EXTRACT(Config, '$."DataBits"'))) = ''));

UPDATE protocol_configs SET Config = JSON_REMOVE(CAST(Config AS JSON), '$."TimeoutSeconds"')
WHERE JSON_VALID(Config)
  AND LOWER(REPLACE(REPLACE(REPLACE(Type, '_', ''), '-', ''), ' ', ''))
      IN ('mqtt', 'anshengmqtt', 'modbustcp', 'modbus', 'modbusrtu', 'opcua')
  AND (JSON_TYPE(JSON_EXTRACT(Config, '$."TimeoutSeconds"')) = 'NULL'
  OR (JSON_TYPE(JSON_EXTRACT(Config, '$."TimeoutSeconds"')) = 'STRING'
      AND TRIM(JSON_UNQUOTE(JSON_EXTRACT(Config, '$."TimeoutSeconds"'))) = ''));

UPDATE protocol_configs SET Config = JSON_REMOVE(CAST(Config AS JSON), '$."KeepAliveSeconds"')
WHERE JSON_VALID(Config)
  AND LOWER(REPLACE(REPLACE(REPLACE(Type, '_', ''), '-', ''), ' ', ''))
      IN ('mqtt', 'anshengmqtt', 'modbustcp', 'modbus', 'modbusrtu', 'opcua')
  AND (JSON_TYPE(JSON_EXTRACT(Config, '$."KeepAliveSeconds"')) = 'NULL'
  OR (JSON_TYPE(JSON_EXTRACT(Config, '$."KeepAliveSeconds"')) = 'STRING'
      AND TRIM(JSON_UNQUOTE(JSON_EXTRACT(Config, '$."KeepAliveSeconds"'))) = ''));

UPDATE protocol_configs SET Config = JSON_REMOVE(CAST(Config AS JSON), '$."CommandMinIntervalMs"')
WHERE JSON_VALID(Config)
  AND LOWER(REPLACE(REPLACE(REPLACE(Type, '_', ''), '-', ''), ' ', ''))
      IN ('mqtt', 'anshengmqtt', 'modbustcp', 'modbus', 'modbusrtu', 'opcua')
  AND (JSON_TYPE(JSON_EXTRACT(Config, '$."CommandMinIntervalMs"')) = 'NULL'
  OR (JSON_TYPE(JSON_EXTRACT(Config, '$."CommandMinIntervalMs"')) = 'STRING'
      AND TRIM(JSON_UNQUOTE(JSON_EXTRACT(Config, '$."CommandMinIntervalMs"'))) = ''));

-- --------------------------------------------------------------------------------------------
-- 2.3 非数值属性的 JSON null → 删键（规则 0：字符串/布尔属性同样删）
--
--     依据：显式 null 会覆盖属性初始化器，{"Host":null} 绑定出来是 null 而不是 "localhost"，
--     结果比「键不存在」更坏。故一律删掉，让 DTO 默认值生效。
--
--     ⚠️ 注意：这里【只删 JSON null，不删空串】。空串对 string Host 是合法值，
--        与 2.2 的数值属性不同，不能一起删（对应规则 4）。
--
--     已知限制：MySQL 纯 SQL 无法动态遍历对象的全部键，故此处逐一枚举各 DTO 的已知属性。
--     C# 侧对任意「未知键」的 null 也会删除（见测试 JsonNull_OnUnknownKey_IsAlsoRemoved），
--     此处覆盖不到的极少数未知 null 键会残留在库里；但它们本就是 DTO 的未知成员，
--     反序列化时被 System.Text.Json 忽略，不影响绑定结果，可接受。
-- --------------------------------------------------------------------------------------------

-- MQTT / 安圣 MQTT 的字符串与布尔属性
UPDATE protocol_configs SET Config = JSON_REMOVE(CAST(Config AS JSON), '$."Host"')
WHERE JSON_VALID(Config)
  AND LOWER(REPLACE(REPLACE(REPLACE(Type,'_',''),'-',''),' ','')) IN ('mqtt','anshengmqtt','modbustcp','modbus','modbusrtu','opcua')
  AND JSON_TYPE(JSON_EXTRACT(Config, '$."Host"')) = 'NULL';

UPDATE protocol_configs SET Config = JSON_REMOVE(CAST(Config AS JSON), '$."Username"')
WHERE JSON_VALID(Config)
  AND LOWER(REPLACE(REPLACE(REPLACE(Type,'_',''),'-',''),' ','')) IN ('mqtt','anshengmqtt','modbustcp','modbus','modbusrtu','opcua')
  AND JSON_TYPE(JSON_EXTRACT(Config, '$."Username"')) = 'NULL';

UPDATE protocol_configs SET Config = JSON_REMOVE(CAST(Config AS JSON), '$."Password"')
WHERE JSON_VALID(Config)
  AND LOWER(REPLACE(REPLACE(REPLACE(Type,'_',''),'-',''),' ','')) IN ('mqtt','anshengmqtt','modbustcp','modbus','modbusrtu','opcua')
  AND JSON_TYPE(JSON_EXTRACT(Config, '$."Password"')) = 'NULL';

UPDATE protocol_configs SET Config = JSON_REMOVE(CAST(Config AS JSON), '$."ClientIdPrefix"')
WHERE JSON_VALID(Config)
  AND LOWER(REPLACE(REPLACE(REPLACE(Type,'_',''),'-',''),' ','')) IN ('mqtt','anshengmqtt','modbustcp','modbus','modbusrtu','opcua')
  AND JSON_TYPE(JSON_EXTRACT(Config, '$."ClientIdPrefix"')) = 'NULL';

UPDATE protocol_configs SET Config = JSON_REMOVE(CAST(Config AS JSON), '$."CleanSession"')
WHERE JSON_VALID(Config)
  AND LOWER(REPLACE(REPLACE(REPLACE(Type,'_',''),'-',''),' ','')) IN ('mqtt','anshengmqtt','modbustcp','modbus','modbusrtu','opcua')
  AND JSON_TYPE(JSON_EXTRACT(Config, '$."CleanSession"')) = 'NULL';

UPDATE protocol_configs SET Config = JSON_REMOVE(CAST(Config AS JSON), '$."SubscribeTopics"')
WHERE JSON_VALID(Config)
  AND LOWER(REPLACE(REPLACE(REPLACE(Type,'_',''),'-',''),' ','')) IN ('mqtt','anshengmqtt','modbustcp','modbus','modbusrtu','opcua')
  AND JSON_TYPE(JSON_EXTRACT(Config, '$."SubscribeTopics"')) = 'NULL';

UPDATE protocol_configs SET Config = JSON_REMOVE(CAST(Config AS JSON), '$."CommandTopicTemplate"')
WHERE JSON_VALID(Config)
  AND LOWER(REPLACE(REPLACE(REPLACE(Type,'_',''),'-',''),' ','')) IN ('mqtt','anshengmqtt','modbustcp','modbus','modbusrtu','opcua')
  AND JSON_TYPE(JSON_EXTRACT(Config, '$."CommandTopicTemplate"')) = 'NULL';

UPDATE protocol_configs SET Config = JSON_REMOVE(CAST(Config AS JSON), '$."CommandResponseTopic"')
WHERE JSON_VALID(Config)
  AND LOWER(REPLACE(REPLACE(REPLACE(Type,'_',''),'-',''),' ','')) IN ('mqtt','anshengmqtt','modbustcp','modbus','modbusrtu','opcua')
  AND JSON_TYPE(JSON_EXTRACT(Config, '$."CommandResponseTopic"')) = 'NULL';

UPDATE protocol_configs SET Config = JSON_REMOVE(CAST(Config AS JSON), '$."ReadTopicTemplate"')
WHERE JSON_VALID(Config)
  AND LOWER(REPLACE(REPLACE(REPLACE(Type,'_',''),'-',''),' ','')) IN ('mqtt','anshengmqtt','modbustcp','modbus','modbusrtu','opcua')
  AND JSON_TYPE(JSON_EXTRACT(Config, '$."ReadTopicTemplate"')) = 'NULL';

UPDATE protocol_configs SET Config = JSON_REMOVE(CAST(Config AS JSON), '$."PublishTopicPattern"')
WHERE JSON_VALID(Config)
  AND LOWER(REPLACE(REPLACE(REPLACE(Type,'_',''),'-',''),' ','')) IN ('mqtt','anshengmqtt','modbustcp','modbus','modbusrtu','opcua')
  AND JSON_TYPE(JSON_EXTRACT(Config, '$."PublishTopicPattern"')) = 'NULL';

UPDATE protocol_configs SET Config = JSON_REMOVE(CAST(Config AS JSON), '$."WillTopicPattern"')
WHERE JSON_VALID(Config)
  AND LOWER(REPLACE(REPLACE(REPLACE(Type,'_',''),'-',''),' ','')) IN ('mqtt','anshengmqtt','modbustcp','modbus','modbusrtu','opcua')
  AND JSON_TYPE(JSON_EXTRACT(Config, '$."WillTopicPattern"')) = 'NULL';

UPDATE protocol_configs SET Config = JSON_REMOVE(CAST(Config AS JSON), '$."SubscribeTopicTemplate"')
WHERE JSON_VALID(Config)
  AND LOWER(REPLACE(REPLACE(REPLACE(Type,'_',''),'-',''),' ','')) IN ('mqtt','anshengmqtt','modbustcp','modbus','modbusrtu','opcua')
  AND JSON_TYPE(JSON_EXTRACT(Config, '$."SubscribeTopicTemplate"')) = 'NULL';

UPDATE protocol_configs SET Config = JSON_REMOVE(CAST(Config AS JSON), '$."AutoConfigureAutoReport"')
WHERE JSON_VALID(Config)
  AND LOWER(REPLACE(REPLACE(REPLACE(Type,'_',''),'-',''),' ','')) IN ('mqtt','anshengmqtt','modbustcp','modbus','modbusrtu','opcua')
  AND JSON_TYPE(JSON_EXTRACT(Config, '$."AutoConfigureAutoReport"')) = 'NULL';

UPDATE protocol_configs SET Config = JSON_REMOVE(CAST(Config AS JSON), '$."DefaultAutoReport"')
WHERE JSON_VALID(Config)
  AND LOWER(REPLACE(REPLACE(REPLACE(Type,'_',''),'-',''),' ','')) IN ('mqtt','anshengmqtt','modbustcp','modbus','modbusrtu','opcua')
  AND JSON_TYPE(JSON_EXTRACT(Config, '$."DefaultAutoReport"')) = 'NULL';

-- Modbus RTU 专有
UPDATE protocol_configs SET Config = JSON_REMOVE(CAST(Config AS JSON), '$."PortName"')
WHERE JSON_VALID(Config)
  AND LOWER(REPLACE(REPLACE(REPLACE(Type,'_',''),'-',''),' ','')) IN ('mqtt','anshengmqtt','modbustcp','modbus','modbusrtu','opcua')
  AND JSON_TYPE(JSON_EXTRACT(Config, '$."PortName"')) = 'NULL';

UPDATE protocol_configs SET Config = JSON_REMOVE(CAST(Config AS JSON), '$."StopBits"')
WHERE JSON_VALID(Config)
  AND LOWER(REPLACE(REPLACE(REPLACE(Type,'_',''),'-',''),' ','')) IN ('mqtt','anshengmqtt','modbustcp','modbus','modbusrtu','opcua')
  AND JSON_TYPE(JSON_EXTRACT(Config, '$."StopBits"')) = 'NULL';

UPDATE protocol_configs SET Config = JSON_REMOVE(CAST(Config AS JSON), '$."Parity"')
WHERE JSON_VALID(Config)
  AND LOWER(REPLACE(REPLACE(REPLACE(Type,'_',''),'-',''),' ','')) IN ('mqtt','anshengmqtt','modbustcp','modbus','modbusrtu','opcua')
  AND JSON_TYPE(JSON_EXTRACT(Config, '$."Parity"')) = 'NULL';

-- OPC UA 专有
UPDATE protocol_configs SET Config = JSON_REMOVE(CAST(Config AS JSON), '$."EndpointUrl"')
WHERE JSON_VALID(Config)
  AND LOWER(REPLACE(REPLACE(REPLACE(Type,'_',''),'-',''),' ','')) IN ('mqtt','anshengmqtt','modbustcp','modbus','modbusrtu','opcua')
  AND JSON_TYPE(JSON_EXTRACT(Config, '$."EndpointUrl"')) = 'NULL';

UPDATE protocol_configs SET Config = JSON_REMOVE(CAST(Config AS JSON), '$."UsePollingMode"')
WHERE JSON_VALID(Config)
  AND LOWER(REPLACE(REPLACE(REPLACE(Type,'_',''),'-',''),' ','')) IN ('mqtt','anshengmqtt','modbustcp','modbus','modbusrtu','opcua')
  AND JSON_TYPE(JSON_EXTRACT(Config, '$."UsePollingMode"')) = 'NULL';

UPDATE protocol_configs SET Config = JSON_REMOVE(CAST(Config AS JSON), '$."SecurityPolicy"')
WHERE JSON_VALID(Config)
  AND LOWER(REPLACE(REPLACE(REPLACE(Type,'_',''),'-',''),' ','')) IN ('mqtt','anshengmqtt','modbustcp','modbus','modbusrtu','opcua')
  AND JSON_TYPE(JSON_EXTRACT(Config, '$."SecurityPolicy"')) = 'NULL';

UPDATE protocol_configs SET Config = JSON_REMOVE(CAST(Config AS JSON), '$."SecurityMode"')
WHERE JSON_VALID(Config)
  AND LOWER(REPLACE(REPLACE(REPLACE(Type,'_',''),'-',''),' ','')) IN ('mqtt','anshengmqtt','modbustcp','modbus','modbusrtu','opcua')
  AND JSON_TYPE(JSON_EXTRACT(Config, '$."SecurityMode"')) = 'NULL';

UPDATE protocol_configs SET Config = JSON_REMOVE(CAST(Config AS JSON), '$."CertificatePath"')
WHERE JSON_VALID(Config)
  AND LOWER(REPLACE(REPLACE(REPLACE(Type,'_',''),'-',''),' ','')) IN ('mqtt','anshengmqtt','modbustcp','modbus','modbusrtu','opcua')
  AND JSON_TYPE(JSON_EXTRACT(Config, '$."CertificatePath"')) = 'NULL';

UPDATE protocol_configs SET Config = JSON_REMOVE(CAST(Config AS JSON), '$."Nodes"')
WHERE JSON_VALID(Config)
  AND LOWER(REPLACE(REPLACE(REPLACE(Type,'_',''),'-',''),' ','')) IN ('mqtt','anshengmqtt','modbustcp','modbus','modbusrtu','opcua')
  AND JSON_TYPE(JSON_EXTRACT(Config, '$."Nodes"')) = 'NULL';

-- Modbus 共有
UPDATE protocol_configs SET Config = JSON_REMOVE(CAST(Config AS JSON), '$."Devices"')
WHERE JSON_VALID(Config)
  AND LOWER(REPLACE(REPLACE(REPLACE(Type,'_',''),'-',''),' ','')) IN ('mqtt','anshengmqtt','modbustcp','modbus','modbusrtu','opcua')
  AND JSON_TYPE(JSON_EXTRACT(Config, '$."Devices"')) = 'NULL';

UPDATE protocol_configs SET Config = JSON_REMOVE(CAST(Config AS JSON), '$."AppCode"')
WHERE JSON_VALID(Config)
  AND LOWER(REPLACE(REPLACE(REPLACE(Type,'_',''),'-',''),' ','')) IN ('mqtt','anshengmqtt','modbustcp','modbus','modbusrtu','opcua')
  AND JSON_TYPE(JSON_EXTRACT(Config, '$."AppCode"')) = 'NULL';

-- ============================================================================================
-- 阶段 3：执行后校验（在 COMMIT 之前跑；3.1～3.4 与 3.6 必须全部返回 0 行，否则 ROLLBACK 并复盘）
-- ============================================================================================

-- 3.1 残留 legacy 小写键（应为 0 行）
SELECT Id, Type, Config
FROM protocol_configs
WHERE JSON_VALID(Config)
  AND LOWER(REPLACE(REPLACE(REPLACE(Type, '_', ''), '-', ''), ' ', ''))
      IN ('mqtt', 'anshengmqtt', 'modbustcp', 'modbus', 'modbusrtu', 'opcua')
  AND JSON_CONTAINS_PATH(Config, 'one',
        '$."host"', '$."port"', '$."endpoint"', '$."username"', '$."password"',
        '$."clientidprefix"', '$."clientIdPrefix"', '$."cleansession"', '$."cleanSession"',
        '$."qoslevel"', '$."qosLevel"', '$."serialport"', '$."serialPort"',
        '$."baudrate"', '$."baudRate"');

-- 3.2 仍为字符串型的数值属性（应为 0 行；若有，说明值不是合法整数，如 "50x2"，需人工确认）
SELECT Id, Type, Config
FROM protocol_configs
WHERE JSON_VALID(Config)
  AND (JSON_TYPE(JSON_EXTRACT(Config, '$."Port"'))                 = 'STRING'
    OR JSON_TYPE(JSON_EXTRACT(Config, '$."BaudRate"'))             = 'STRING'
    OR JSON_TYPE(JSON_EXTRACT(Config, '$."TimeoutMs"'))            = 'STRING'
    OR JSON_TYPE(JSON_EXTRACT(Config, '$."PollIntervalMs"'))       = 'STRING'
    OR JSON_TYPE(JSON_EXTRACT(Config, '$."QosLevel"'))             = 'STRING'
    OR JSON_TYPE(JSON_EXTRACT(Config, '$."DataBits"'))             = 'STRING'
    OR JSON_TYPE(JSON_EXTRACT(Config, '$."TimeoutSeconds"'))       = 'STRING'
    OR JSON_TYPE(JSON_EXTRACT(Config, '$."KeepAliveSeconds"'))     = 'STRING'
    OR JSON_TYPE(JSON_EXTRACT(Config, '$."CommandMinIntervalMs"')) = 'STRING');

-- 3.3 新旧键并存的残留（应为 0 行）
SELECT Id, Type, Config
FROM protocol_configs
WHERE JSON_VALID(Config)
  AND (JSON_CONTAINS_PATH(Config, 'all', '$."host"', '$."Host"')
    OR JSON_CONTAINS_PATH(Config, 'all', '$."port"', '$."Port"')
    OR JSON_CONTAINS_PATH(Config, 'all', '$."endpoint"', '$."EndpointUrl"')
    OR JSON_CONTAINS_PATH(Config, 'all', '$."serialPort"', '$."PortName"')
    OR JSON_CONTAINS_PATH(Config, 'all', '$."baudRate"', '$."BaudRate"'));

-- 3.4 规范键仍为 JSON null 的残留（应为 0 行；规则 0 要求这些键必须已被删除）
SELECT Id, Type, Config
FROM protocol_configs
WHERE JSON_VALID(Config)
  AND LOWER(REPLACE(REPLACE(REPLACE(Type, '_', ''), '-', ''), ' ', ''))
      IN ('mqtt', 'anshengmqtt', 'modbustcp', 'modbus', 'modbusrtu', 'opcua')
  AND (JSON_TYPE(JSON_EXTRACT(Config, '$."Host"'))            = 'NULL'
    OR JSON_TYPE(JSON_EXTRACT(Config, '$."Port"'))            = 'NULL'
    OR JSON_TYPE(JSON_EXTRACT(Config, '$."PortName"'))        = 'NULL'
    OR JSON_TYPE(JSON_EXTRACT(Config, '$."BaudRate"'))        = 'NULL'
    OR JSON_TYPE(JSON_EXTRACT(Config, '$."EndpointUrl"'))     = 'NULL'
    OR JSON_TYPE(JSON_EXTRACT(Config, '$."Username"'))        = 'NULL'
    OR JSON_TYPE(JSON_EXTRACT(Config, '$."Password"'))        = 'NULL'
    OR JSON_TYPE(JSON_EXTRACT(Config, '$."ClientIdPrefix"'))  = 'NULL'
    OR JSON_TYPE(JSON_EXTRACT(Config, '$."CleanSession"'))    = 'NULL'
    OR JSON_TYPE(JSON_EXTRACT(Config, '$."QosLevel"'))        = 'NULL'
    OR JSON_TYPE(JSON_EXTRACT(Config, '$."TimeoutMs"'))       = 'NULL'
    OR JSON_TYPE(JSON_EXTRACT(Config, '$."PollIntervalMs"'))  = 'NULL');

-- 3.6 数值属性超出 Int32 范围的残留（应为 0 行）
--
--     ⚠️ 为什么单独查这一条：3.2 只能查出"仍是字符串"的值，而超范围的值在阶段 2 会被
--        "成功"矫正成 JSON 数字，于是 3.2 查不出来，却依然绑定失败 —— 假信号。
--        所有数值 DTO 属性都是 C# int（Int32），范围 [-2147483648, 2147483647]。
--        归一化器用的是 long.TryParse，容得下但 DTO 装不下。
--     已由测试 NumericProperty_OutOfInt32Range_IsCoercedButStillFailsToBind_KnownGap 钉死。
--     查出来的行需人工核实真实值（多半是脏数据或单位写错，如把秒写成了纳秒）。
SELECT Id, Type, Config
FROM protocol_configs
WHERE JSON_VALID(Config)
  AND LOWER(REPLACE(REPLACE(REPLACE(Type, '_', ''), '-', ''), ' ', ''))
      IN ('mqtt', 'anshengmqtt', 'modbustcp', 'modbus', 'modbusrtu', 'opcua')
  AND (JSON_EXTRACT(Config, '$."Port"')                 NOT BETWEEN -2147483648 AND 2147483647
    OR JSON_EXTRACT(Config, '$."BaudRate"')             NOT BETWEEN -2147483648 AND 2147483647
    OR JSON_EXTRACT(Config, '$."TimeoutMs"')            NOT BETWEEN -2147483648 AND 2147483647
    OR JSON_EXTRACT(Config, '$."PollIntervalMs"')       NOT BETWEEN -2147483648 AND 2147483647
    OR JSON_EXTRACT(Config, '$."QosLevel"')             NOT BETWEEN -2147483648 AND 2147483647
    OR JSON_EXTRACT(Config, '$."DataBits"')             NOT BETWEEN -2147483648 AND 2147483647
    OR JSON_EXTRACT(Config, '$."TimeoutSeconds"')       NOT BETWEEN -2147483648 AND 2147483647
    OR JSON_EXTRACT(Config, '$."KeepAliveSeconds"')     NOT BETWEEN -2147483648 AND 2147483647
    OR JSON_EXTRACT(Config, '$."CommandMinIntervalMs"') NOT BETWEEN -2147483648 AND 2147483647);

-- 3.5 迁移后全量抽样，人工核对（可选，不要求 0 行）
SELECT Id, Type, Config
FROM protocol_configs
WHERE JSON_VALID(Config)
  AND LOWER(REPLACE(REPLACE(REPLACE(Type, '_', ''), '-', ''), ' ', ''))
      IN ('mqtt', 'anshengmqtt', 'modbustcp', 'modbus', 'modbusrtu', 'opcua')
ORDER BY Id;


-- ============================================================================================
-- 阶段 4：提交 / 回滚
--   3.1～3.4 与 3.6 全部 0 行 → COMMIT；任一非 0 → ROLLBACK，把结果反馈给研发再改脚本。
--   （3.5 是抽样，不要求 0 行）
--   ⚠️ COMMIT 之后无法用 ROLLBACK 撤销，只能靠阶段 0 之前做的备份 / 快照表恢复。
-- ============================================================================================

COMMIT;
-- ROLLBACK;   -- 校验不通过时改用这一行（并把上面的 COMMIT 注释掉）
