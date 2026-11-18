-- =====================================================================
-- 迁移：AddCameraEncryptionCode
-- 对应 EF 迁移 id：20261114000000_AddCameraEncryptionCode
-- 作用：给 cameras 表新增 EncryptionCode 列（varchar(200) NULL），
--       用于存储萤石设备验证码(ewcode)，修复加密摄像头取流 60019。
-- 说明：本脚本可独立于 `dotnet ef database update` 直接执行；
--       已做幂等处理（列/迁移历史已存在则跳过），可重复执行。
--       同时写入 __EFMigrationsHistory，避免后续 `dotnet ef database update`
--       把该迁移误判为"未应用"而再次 ALTER 报错。
-- 适用：MySQL（Pomelo.EntityFrameworkCore.MySql）。请在目标库上执行。
-- =====================================================================

SET @db  = DATABASE();
SET @tbl = 'cameras';
SET @col = 'EncryptionCode';

-- 1) 幂等加列 -----------------------------------------------------------
SET @col_exists = (
    SELECT COUNT(*) FROM information_schema.COLUMNS
    WHERE TABLE_SCHEMA = @db AND TABLE_NAME = @tbl AND COLUMN_NAME = @col
);
SET @sql_add = IF(
    @col_exists = 0,
    'ALTER TABLE `cameras` ADD COLUMN `EncryptionCode` varchar(200) NULL',
    'SELECT 1'
);
PREPARE stmt_add FROM @sql_add;
EXECUTE stmt_add;
DEALLOCATE PREPARE stmt_add;

-- 2) 登记 EF 迁移历史（幂等）-------------------------------------------
INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
SELECT '20261114000000_AddCameraEncryptionCode', '8.0.0'
WHERE NOT EXISTS (
    SELECT 1 FROM `__EFMigrationsHistory`
    WHERE `MigrationId` = '20261114000000_AddCameraEncryptionCode'
);

-- 校验
SELECT
    'cameras.EncryptionCode'        AS item,
    (
        SELECT COUNT(*) FROM information_schema.COLUMNS
        WHERE TABLE_SCHEMA = @db AND TABLE_NAME = @tbl AND COLUMN_NAME = @col
    )                               AS column_present,
    (
        SELECT COUNT(*) FROM `__EFMigrationsHistory`
        WHERE `MigrationId` = '20261114000000_AddCameraEncryptionCode'
    )                               AS migration_registered;
