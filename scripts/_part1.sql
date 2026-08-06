-- ============================================================================================
-- ██  警  告  ██  本脚本由 AI 生成，未在任何数据库上执行过（编写过程中未连接过任何 MySQL 实例）。
-- ============================================================================================
--   * 上线前必须由 DBA 逐条审阅，并在**完整备份之后**、**事务中**手工执行。
--   * 必须先在与生产同版本、同数据分布的**预发/影子库**上跑通并核对结果，再动生产。
--   * 回滚：本脚本**原地改写 JSON 文本**，JSON_REMOVE 掉的旧键无法从改写后的数据还原，
--     事务一旦 COMMIT 就只能靠备份恢复。执行前请确认备份可用且恢复流程已演练。
--   * 建议备份方式（二选一，推荐两个都做）：
--       ① 库内备份表（最省事，回滚最快；执行前必做，把 YYYYMMDD 换成当天日期）：
--            CREATE TABLE protocol_configs_backup_YYYYMMDD AS SELECT * FROM protocol_configs;
--          回滚时：
--            UPDATE protocol_configs pc
--              JOIN protocol_configs_backup_YYYYMMDD b ON b.Id = pc.Id
--              SET pc.Config = b.Config;
--          （注意 CREATE TABLE ... AS SELECT 不带索引/主键，仅作数据快照；用完记得清理。）
--       ② 文件级备份（异地留档）：
--            mysqldump -h <host> -u <user> -p --single-transaction --set-gtid-purged=OFF \
--                      iot_platform protocol_configs > protocol_configs_backup_$(date +%Y%m%d%H%M).sql
-- ============================================================================================
--
-- 【目的】
--   把 protocol_configs.Config（longtext，存 JSON 文本）里的存量「小写/camelCase 键 + 字符串型数值」
--   归一为「PascalCase 键 + 正确 JSON 类型」，与各协议 DTO（MqttProtocolOptions / AnShengMqttProtocolOptions /
--   ModbusTcpOptions / ModbusRtuOptions / OpcUaOptions）的属性名对齐。
--
-- 【为什么必须做】
--   旧版协议管理页的通用表单写入的是小写键 + 字符串值，例如 {"host":"1.2.3.4","port":"502"}。
--   后端给适配器注入大小写不敏感反序列化后，键名问题解决了，但值类型问题没解决。
--
-- 【与运行时兜底的关系 —— 本脚本仍然必要】
--   Infrastructure/Protocol/Adapters/ProtocolJsonOptions.cs 现已追加
--   NumberHandling = AllowReadingFromString，使字符串型数字在**运行时**不再抛异常。
--   那是**运行时兜底**，不是数据清洗的替代（该文件注释第 ④ 条亦如此声明）：
--     · 兜底只保证「SQL 从未执行过的环境」也连得上，库里的数据依然是脏的；
--     · 兜底**不解决 null 覆盖默认值**的问题（见下方「规则 0 的依据」）；
--     · 兜底**不解决新旧键并存时取值依赖键顺序**的不确定性 —— 大小写不敏感下
--       {"port":"502","Port":5502} 是「后出现者覆盖」，取到哪个值随键顺序漂移（已实测）；
--     · 兜底随时可能因为「收紧类型契约」被移除，届时数据清洗就是唯一防线。
--   因此本脚本与运行时兜底**并存，都要保留**。
--
-- 【与代码的对应关系】
--   本脚本是 Data/ProtocolConfigNormalizer.cs 的 SQL 侧等价实现（覆盖已观测到的存量键集合），
--   前端 Web/src/app/pages/ProtocolManagementPage.tsx 的 normalizeLegacyConfigKeys 是第三份同构实现。
--   **三方规则必须逐条一致**，改一处就要同步另外两处。
--   C# 归一化器是**权威实现且覆盖面更广**（能大小写不敏感匹配任意 DTO 规范属性名）；
--   MySQL 的 JSON 路径是**大小写敏感**的，无法穷举所有大小写变体，所以本脚本只处理明确列出的键。
--   若脚本执行后仍有零星异常行，用 C# 归一化器兜底重跑一遍即可（两者对同一输入语义一致且幂等）。
--
-- 【重要行为约定】（与 C# / 前端实现三方逐条一致）
--   0. **JSON null 一律删键**，适用于**全部**属性（不只数值属性）。见下方「规则 0 的依据」。
--   1. 冲突时**规范键（精确 PascalCase）优先**：{"port":"502","Port":5502} → {"Port":5502}。
--      **但值为 null 的规范键视同不存在**：{"host":"1.2.3.4","Host":null} → {"Host":"1.2.3.4"}（rescue）。
--      否则我们手上明明有真实值，却把它丢掉换成一个连默认值都不如的 null，是净损失。
--   2. 旧键在改名后**一律删除**，不保留冗余副本。
--   3. 未列出的键**原样保留**，不删不改。
--   4. 数值属性为字符串且是合法整数 → 转数字；为**空串** → **删除该键**（让 DTO 默认值生效，
--      因为 {"Port":""} 绑定到 int 会抛异常）；非整数格式字符串 → **保持原值**（不猜测）。
--      注意空串删键**只作用于数值属性** —— 空串对 string Host 是合法值，不能一起删。
--   5. 幂等：脚本可安全重复执行，第二次执行影响 0 行。
--
-- 【规则 0 的依据 —— 为什么 null 必须删而不是保留】
--   System.Text.Json 遇到显式 null 会**覆盖**属性初始化器给的默认值：
--   public string Host { get; set; } = "localhost" 碰上 {"Host":null}，绑定结果是 **null**，不是 "localhost"。
--   也就是说保留 null 会产出「比默认值更坏」的结果；数值属性上更是直接抛 JsonException。
--   该行为已由后端测试 ExplicitJsonNull_OverridesPropertyInitializerDefault_NotIgnored 实测证明。
--
-- 【已知副作用 / 覆盖边界】
--   * MySQL 的 JSON 类型会**重排键顺序**（按 key 长度、再按字典序）并压缩空白，
--     改写后的 Config 文本外观会变化。语义不变，但如果有任何逻辑依赖 Config 的原始文本形态
--     （字符串比对、指纹/哈希、审计 diff），请提前评估。C# 归一化器则保持原始键顺序。
--   * 规则 0 在 SQL 侧**只对下方阶段 2.3 枚举的已知属性名生效**；C# 侧对**任意**键（含业务自定义
--     扩展键）的 null 都会删。若库里存在自定义键带 null，SQL 不会清掉，需用 C# 归一化器兜底。
--   * Config 里非法 JSON 的脏数据行会被 JSON_VALID(Config) 过滤掉，不做任何改动（需人工处理）。
--   * 本脚本不改 UpdatedAt（避免污染业务时间戳）；若审计要求必须刷新，请 DBA 自行追加。
--
-- 【环境要求】
--   MySQL 5.7.8+ / 8.0（用到 JSON_VALID / JSON_TYPE / JSON_EXTRACT / JSON_SET / JSON_REMOVE /
--   JSON_CONTAINS_PATH / JSON_UNQUOTE / CAST(... AS UNSIGNED)，均为 5.7.8 起可用；
--   未使用 CTE / 窗口函数 / JSON_TABLE，兼容 5.7.26）。
--   目标库：iot_platform，目标表：protocol_configs，目标列：Config（longtext）。
-- ============================================================================================

-- 若客户端未选库，取消下一行注释：
-- USE iot_platform;

-- ============================================================================================
-- 阶段 0：执行前盘点（只读，建议先单独跑一遍，把结果贴到变更单里）
-- ============================================================================================

-- 0.1 非法 JSON 的脏数据行（本脚本不会碰它们，需人工处理）
SELECT Id, Type, LEFT(Config, 120) AS ConfigHead
FROM protocol_configs
WHERE Config IS NOT NULL
  AND TRIM(Config) <> ''
  AND NOT JSON_VALID(Config);

-- 0.2 含 legacy 小写/camelCase 键的行数（按协议类型汇总）—— 这就是本次会改动的行范围
SELECT Type, COUNT(*) AS LegacyRowCount
FROM protocol_configs
WHERE JSON_VALID(Config)
  AND JSON_CONTAINS_PATH(Config, 'one',
        '$."host"', '$."port"', '$."endpoint"', '$."username"', '$."password"',
        '$."clientidprefix"', '$."clientIdPrefix"', '$."cleansession"', '$."cleanSession"',
        '$."qoslevel"', '$."qosLevel"', '$."serialport"', '$."serialPort"',
        '$."baudrate"', '$."baudRate"')
GROUP BY Type;

-- 0.3 数值属性被存成字符串的行（没有运行时兜底时，这些就是会抛 JsonException 的元凶）
SELECT Id, Type, Config
FROM protocol_configs
WHERE JSON_VALID(Config)
  AND (JSON_TYPE(JSON_EXTRACT(Config, '$."Port"'))       = 'STRING'
    OR JSON_TYPE(JSON_EXTRACT(Config, '$."port"'))       = 'STRING'
    OR JSON_TYPE(JSON_EXTRACT(Config, '$."BaudRate"'))   = 'STRING'
    OR JSON_TYPE(JSON_EXTRACT(Config, '$."baudRate"'))   = 'STRING'
    OR JSON_TYPE(JSON_EXTRACT(Config, '$."TimeoutMs"'))  = 'STRING'
    OR JSON_TYPE(JSON_EXTRACT(Config, '$."QosLevel"'))   = 'STRING');

-- 0.4 含 JSON null 值的键（规则 0 会删掉它们）
--     请特别关注 RescueCandidate = 1 的行：迁移后它们的值会从 null 变成同名小写键里的真实值，
--     这是**预期行为**（rescue），不是数据被改坏。
SELECT Id, Type, Config,
       CASE WHEN (JSON_TYPE(JSON_EXTRACT(Config, '$."Host"')) = 'NULL'
                  AND JSON_CONTAINS_PATH(Config, 'one', '$."host"'))
              OR (JSON_TYPE(JSON_EXTRACT(Config, '$."Port"')) = 'NULL'
                  AND JSON_CONTAINS_PATH(Config, 'one', '$."port"'))
              OR (JSON_TYPE(JSON_EXTRACT(Config, '$."EndpointUrl"')) = 'NULL'
                  AND JSON_CONTAINS_PATH(Config, 'one', '$."endpoint"'))
              OR (JSON_TYPE(JSON_EXTRACT(Config, '$."PortName"')) = 'NULL'
                  AND JSON_CONTAINS_PATH(Config, 'one', '$."serialPort"', '$."serialport"'))
              OR (JSON_TYPE(JSON_EXTRACT(Config, '$."BaudRate"')) = 'NULL'
                  AND JSON_CONTAINS_PATH(Config, 'one', '$."baudRate"', '$."baudrate"'))
            THEN 1 ELSE 0 END AS RescueCandidate
FROM protocol_configs
WHERE JSON_VALID(Config)
  AND JSON_TYPE(CAST(Config AS JSON)) = 'OBJECT'
  AND (JSON_TYPE(JSON_EXTRACT(Config, '$."Host"'))            = 'NULL'
    OR JSON_TYPE(JSON_EXTRACT(Config, '$."Port"'))            = 'NULL'
    OR JSON_TYPE(JSON_EXTRACT(Config, '$."EndpointUrl"'))     = 'NULL'
    OR JSON_TYPE(JSON_EXTRACT(Config, '$."PortName"'))        = 'NULL'
    OR JSON_TYPE(JSON_EXTRACT(Config, '$."BaudRate"'))        = 'NULL'
    OR JSON_TYPE(JSON_EXTRACT(Config, '$."Username"'))        = 'NULL'
    OR JSON_TYPE(JSON_EXTRACT(Config, '$."Password"'))        = 'NULL'
    OR JSON_TYPE(JSON_EXTRACT(Config, '$."CertificatePath"')) = 'NULL');
