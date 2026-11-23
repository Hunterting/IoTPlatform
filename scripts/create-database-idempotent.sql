CREATE TABLE IF NOT EXISTS `__EFMigrationsHistory` (
    `MigrationId` varchar(150) CHARACTER SET utf8mb4 NOT NULL,
    `ProductVersion` varchar(32) CHARACTER SET utf8mb4 NOT NULL,
    CONSTRAINT `PK___EFMigrationsHistory` PRIMARY KEY (`MigrationId`)
) CHARACTER SET=utf8mb4;

START TRANSACTION;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260330025826_Initial') THEN

    ALTER DATABASE CHARACTER SET utf8mb4;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260330025826_Initial') THEN

    CREATE TABLE `customers` (
        `Id` bigint NOT NULL AUTO_INCREMENT,
        `Name` varchar(200) CHARACTER SET utf8mb4 NOT NULL,
        `Code` varchar(50) CHARACTER SET utf8mb4 NOT NULL,
        `AppCode` varchar(50) CHARACTER SET utf8mb4 NOT NULL,
        `ContactPerson` varchar(100) CHARACTER SET utf8mb4 NULL,
        `ContactPhone` varchar(20) CHARACTER SET utf8mb4 NULL,
        `ContactEmail` varchar(100) CHARACTER SET utf8mb4 NULL,
        `Phone` varchar(20) CHARACTER SET utf8mb4 NULL,
        `Address` varchar(500) CHARACTER SET utf8mb4 NULL,
        `Industry` varchar(100) CHARACTER SET utf8mb4 NULL,
        `LicenseKey` varchar(100) CHARACTER SET utf8mb4 NULL,
        `LicenseExpireDate` datetime(6) NULL,
        `MaxDeviceCount` int NOT NULL,
        `MaxUserCount` int NOT NULL,
        `MaxAreaCount` int NOT NULL,
        `Status` varchar(20) CHARACTER SET utf8mb4 NOT NULL,
        `CreatedAt` datetime(6) NOT NULL,
        `UpdatedAt` datetime(6) NOT NULL,
        CONSTRAINT `PK_customers` PRIMARY KEY (`Id`)
    ) CHARACTER SET=utf8mb4;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260330025826_Initial') THEN

    CREATE TABLE `dictionary_types` (
        `Id` bigint NOT NULL AUTO_INCREMENT,
        `Code` varchar(50) CHARACTER SET utf8mb4 NOT NULL,
        `Name` varchar(100) CHARACTER SET utf8mb4 NOT NULL,
        `Description` varchar(500) CHARACTER SET utf8mb4 NULL,
        `SortOrder` int NOT NULL,
        `IsActive` tinyint(1) NOT NULL,
        `AppCode` varchar(255) CHARACTER SET utf8mb4 NULL,
        `CreatedAt` datetime(6) NOT NULL,
        `UpdatedAt` datetime(6) NOT NULL,
        CONSTRAINT `PK_dictionary_types` PRIMARY KEY (`Id`)
    ) CHARACTER SET=utf8mb4;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260330025826_Initial') THEN

    CREATE TABLE `login_logs` (
        `Id` bigint NOT NULL AUTO_INCREMENT,
        `UserId` bigint NULL,
        `Email` varchar(200) CHARACTER SET utf8mb4 NULL,
        `UserName` varchar(100) CHARACTER SET utf8mb4 NULL,
        `Role` varchar(100) CHARACTER SET utf8mb4 NULL,
        `IpAddress` varchar(50) CHARACTER SET utf8mb4 NULL,
        `IP` varchar(50) CHARACTER SET utf8mb4 NULL,
        `UserAgent` varchar(500) CHARACTER SET utf8mb4 NULL,
        `Status` varchar(20) CHARACTER SET utf8mb4 NULL,
        `FailureReason` varchar(500) CHARACTER SET utf8mb4 NULL,
        `LoginTime` datetime(6) NOT NULL,
        `Location` varchar(50) CHARACTER SET utf8mb4 NULL,
        `AppCode` longtext CHARACTER SET utf8mb4 NULL,
        CONSTRAINT `PK_login_logs` PRIMARY KEY (`Id`)
    ) CHARACTER SET=utf8mb4;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260330025826_Initial') THEN

    CREATE TABLE `operation_logs` (
        `Id` bigint NOT NULL AUTO_INCREMENT,
        `UserId` bigint NOT NULL,
        `UserName` varchar(100) CHARACTER SET utf8mb4 NULL,
        `Role` varchar(50) CHARACTER SET utf8mb4 NULL,
        `CreatedAt` datetime(6) NOT NULL,
        `OperationTime` datetime(6) NOT NULL,
        `Time` datetime(6) NOT NULL,
        `Module` varchar(50) CHARACTER SET utf8mb4 NULL,
        `Operation` varchar(50) CHARACTER SET utf8mb4 NOT NULL,
        `Action` varchar(50) CHARACTER SET utf8mb4 NOT NULL,
        `Target` varchar(200) CHARACTER SET utf8mb4 NULL,
        `Detail` varchar(2000) CHARACTER SET utf8mb4 NULL,
        `IP` varchar(50) CHARACTER SET utf8mb4 NULL,
        `Status` varchar(20) CHARACTER SET utf8mb4 NOT NULL,
        `Duration` int NULL,
        `AppCode` longtext CHARACTER SET utf8mb4 NULL,
        CONSTRAINT `PK_operation_logs` PRIMARY KEY (`Id`)
    ) CHARACTER SET=utf8mb4;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260330025826_Initial') THEN

    CREATE TABLE `protocol_configs` (
        `Id` bigint NOT NULL AUTO_INCREMENT,
        `Name` varchar(100) CHARACTER SET utf8mb4 NOT NULL,
        `Type` varchar(50) CHARACTER SET utf8mb4 NOT NULL,
        `ProtocolType` varchar(50) CHARACTER SET utf8mb4 NULL,
        `Status` varchar(20) CHARACTER SET utf8mb4 NOT NULL,
        `IsActive` tinyint(1) NOT NULL,
        `DeviceIds` longtext CHARACTER SET utf8mb4 NULL,
        `Config` longtext CHARACTER SET utf8mb4 NULL,
        `Description` varchar(500) CHARACTER SET utf8mb4 NULL,
        `AppCode` varchar(255) CHARACTER SET utf8mb4 NULL,
        `CreatedAt` datetime(6) NOT NULL,
        `UpdatedAt` datetime(6) NOT NULL,
        CONSTRAINT `PK_protocol_configs` PRIMARY KEY (`Id`)
    ) CHARACTER SET=utf8mb4;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260330025826_Initial') THEN

    CREATE TABLE `roles` (
        `Id` bigint NOT NULL AUTO_INCREMENT,
        `Code` varchar(50) CHARACTER SET utf8mb4 NOT NULL,
        `Name` varchar(100) CHARACTER SET utf8mb4 NOT NULL,
        `Description` varchar(500) CHARACTER SET utf8mb4 NULL,
        `Permissions` longtext CHARACTER SET utf8mb4 NULL,
        `DataScope` varchar(10) CHARACTER SET utf8mb4 NOT NULL,
        `AppCode` varchar(255) CHARACTER SET utf8mb4 NULL,
        `IsSystem` tinyint(1) NOT NULL,
        `IsDefault` tinyint(1) NOT NULL,
        `Status` longtext CHARACTER SET utf8mb4 NOT NULL,
        `CreatedAt` datetime(6) NOT NULL,
        `UpdatedAt` datetime(6) NOT NULL,
        CONSTRAINT `PK_roles` PRIMARY KEY (`Id`)
    ) CHARACTER SET=utf8mb4;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260330025826_Initial') THEN

    CREATE TABLE `system_settings` (
        `Id` bigint NOT NULL AUTO_INCREMENT,
        `Key` varchar(100) CHARACTER SET utf8mb4 NOT NULL,
        `Value` longtext CHARACTER SET utf8mb4 NOT NULL,
        `Description` varchar(200) CHARACTER SET utf8mb4 NULL,
        `Category` varchar(50) CHARACTER SET utf8mb4 NULL,
        `ValueType` varchar(20) CHARACTER SET utf8mb4 NULL,
        `AppCode` varchar(255) CHARACTER SET utf8mb4 NULL,
        `IsActive` tinyint(1) NOT NULL,
        `SortOrder` int NOT NULL,
        `CreatedAt` datetime(6) NOT NULL,
        `UpdatedAt` datetime(6) NOT NULL,
        CONSTRAINT `PK_system_settings` PRIMARY KEY (`Id`)
    ) CHARACTER SET=utf8mb4;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260330025826_Initial') THEN

    CREATE TABLE `areas` (
        `Id` bigint NOT NULL AUTO_INCREMENT,
        `Name` varchar(200) CHARACTER SET utf8mb4 NOT NULL,
        `Type` varchar(10) CHARACTER SET utf8mb4 NOT NULL,
        `Image` varchar(500) CHARACTER SET utf8mb4 NULL,
        `ParentId` bigint NULL,
        `CustomerId` bigint NULL,
        `AppCode` varchar(255) CHARACTER SET utf8mb4 NULL,
        `Description` varchar(500) CHARACTER SET utf8mb4 NULL,
        `DeviceCount` int NOT NULL,
        `SortOrder` int NOT NULL,
        `CreatedAt` datetime(6) NOT NULL,
        `UpdatedAt` datetime(6) NOT NULL,
        CONSTRAINT `PK_areas` PRIMARY KEY (`Id`),
        CONSTRAINT `FK_areas_areas_ParentId` FOREIGN KEY (`ParentId`) REFERENCES `areas` (`Id`) ON DELETE RESTRICT,
        CONSTRAINT `FK_areas_customers_CustomerId` FOREIGN KEY (`CustomerId`) REFERENCES `customers` (`Id`)
    ) CHARACTER SET=utf8mb4;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260330025826_Initial') THEN

    CREATE TABLE `projects` (
        `Id` bigint NOT NULL AUTO_INCREMENT,
        `CustomerId` bigint NOT NULL,
        `Name` varchar(200) CHARACTER SET utf8mb4 NOT NULL,
        `Address` varchar(500) CHARACTER SET utf8mb4 NULL,
        `DeviceCount` int NOT NULL,
        `OnlineDate` datetime(6) NULL,
        `Status` varchar(20) CHARACTER SET utf8mb4 NOT NULL,
        `AppCode` varchar(255) CHARACTER SET utf8mb4 NULL,
        `CreatedAt` datetime(6) NOT NULL,
        `UpdatedAt` datetime(6) NOT NULL,
        CONSTRAINT `PK_projects` PRIMARY KEY (`Id`),
        CONSTRAINT `FK_projects_customers_CustomerId` FOREIGN KEY (`CustomerId`) REFERENCES `customers` (`Id`) ON DELETE CASCADE
    ) CHARACTER SET=utf8mb4;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260330025826_Initial') THEN

    CREATE TABLE `dictionary_items` (
        `Id` bigint NOT NULL AUTO_INCREMENT,
        `Type` varchar(50) CHARACTER SET utf8mb4 NOT NULL,
        `TypeCode` varchar(50) CHARACTER SET utf8mb4 NULL,
        `Code` varchar(50) CHARACTER SET utf8mb4 NOT NULL,
        `Name` varchar(100) CHARACTER SET utf8mb4 NOT NULL,
        `Sort` int NOT NULL,
        `Description` varchar(500) CHARACTER SET utf8mb4 NULL,
        `Status` varchar(20) CHARACTER SET utf8mb4 NOT NULL,
        `AppCode` varchar(255) CHARACTER SET utf8mb4 NULL,
        `CreatedAt` datetime(6) NOT NULL,
        `UpdatedAt` datetime(6) NOT NULL,
        `DictionaryTypeConfigId` bigint NULL,
        CONSTRAINT `PK_dictionary_items` PRIMARY KEY (`Id`),
        CONSTRAINT `FK_dictionary_items_dictionary_types_DictionaryTypeConfigId` FOREIGN KEY (`DictionaryTypeConfigId`) REFERENCES `dictionary_types` (`Id`)
    ) CHARACTER SET=utf8mb4;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260330025826_Initial') THEN

    CREATE TABLE `users` (
        `Id` bigint NOT NULL AUTO_INCREMENT,
        `Username` varchar(100) CHARACTER SET utf8mb4 NOT NULL,
        `Password` varchar(500) CHARACTER SET utf8mb4 NOT NULL,
        `Name` varchar(100) CHARACTER SET utf8mb4 NOT NULL,
        `FullName` varchar(100) CHARACTER SET utf8mb4 NOT NULL,
        `Email` varchar(200) CHARACTER SET utf8mb4 NOT NULL,
        `Phone` varchar(20) CHARACTER SET utf8mb4 NULL,
        `Status` varchar(20) CHARACTER SET utf8mb4 NOT NULL,
        `IsSuperAdmin` tinyint(1) NOT NULL,
        `RoleId` bigint NULL,
        `Role` varchar(50) CHARACTER SET utf8mb4 NOT NULL,
        `CustomerId` bigint NULL,
        `AppCode` varchar(255) CHARACTER SET utf8mb4 NULL,
        `Avatar` varchar(200) CHARACTER SET utf8mb4 NULL,
        `AllowedAreaIds` longtext CHARACTER SET utf8mb4 NULL,
        `EnergyTypes` json NULL,
        `IsActive` tinyint(1) NOT NULL,
        `LastLoginTime` datetime(6) NULL,
        `CreatedAt` datetime(6) NOT NULL,
        `UpdatedAt` datetime(6) NOT NULL,
        CONSTRAINT `PK_users` PRIMARY KEY (`Id`),
        CONSTRAINT `FK_users_customers_CustomerId` FOREIGN KEY (`CustomerId`) REFERENCES `customers` (`Id`),
        CONSTRAINT `FK_users_roles_RoleId` FOREIGN KEY (`RoleId`) REFERENCES `roles` (`Id`)
    ) CHARACTER SET=utf8mb4;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260330025826_Initial') THEN

    CREATE TABLE `archives` (
        `Id` bigint NOT NULL AUTO_INCREMENT,
        `Name` varchar(200) CHARACTER SET utf8mb4 NOT NULL,
        `AppCode` varchar(50) CHARACTER SET utf8mb4 NULL,
        `Type` varchar(50) CHARACTER SET utf8mb4 NULL,
        `Size` varchar(20) CHARACTER SET utf8mb4 NULL,
        `Date` datetime(6) NULL,
        `Category` varchar(50) CHARACTER SET utf8mb4 NULL,
        `Is3DModel` tinyint(1) NOT NULL,
        `ProjectId` bigint NULL,
        `AreaId` bigint NULL,
        `ImageUrl` varchar(500) CHARACTER SET utf8mb4 NULL,
        `FilePath` varchar(500) CHARACTER SET utf8mb4 NULL,
        `SceneConfig` longtext CHARACTER SET utf8mb4 NULL,
        `AppCodeTenant` longtext CHARACTER SET utf8mb4 NULL,
        `CreatedAt` datetime(6) NOT NULL,
        `UpdatedAt` datetime(6) NOT NULL,
        CONSTRAINT `PK_archives` PRIMARY KEY (`Id`),
        CONSTRAINT `FK_archives_areas_AreaId` FOREIGN KEY (`AreaId`) REFERENCES `areas` (`Id`)
    ) CHARACTER SET=utf8mb4;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260330025826_Initial') THEN

    CREATE TABLE `cameras` (
        `Id` bigint NOT NULL AUTO_INCREMENT,
        `Name` varchar(200) CHARACTER SET utf8mb4 NOT NULL,
        `AreaId` bigint NULL,
        `StreamUrl` varchar(500) CHARACTER SET utf8mb4 NULL,
        `ImageUrl` varchar(500) CHARACTER SET utf8mb4 NULL,
        `Protocol` varchar(20) CHARACTER SET utf8mb4 NULL,
        `Type` varchar(50) CHARACTER SET utf8mb4 NULL,
        `Resolution` varchar(20) CHARACTER SET utf8mb4 NULL,
        `Status` varchar(20) CHARACTER SET utf8mb4 NOT NULL,
        `AppCode` varchar(255) CHARACTER SET utf8mb4 NULL,
        `CreatedAt` datetime(6) NOT NULL,
        `UpdatedAt` datetime(6) NOT NULL,
        CONSTRAINT `PK_cameras` PRIMARY KEY (`Id`),
        CONSTRAINT `FK_cameras_areas_AreaId` FOREIGN KEY (`AreaId`) REFERENCES `areas` (`Id`)
    ) CHARACTER SET=utf8mb4;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260330025826_Initial') THEN

    CREATE TABLE `contracts` (
        `Id` bigint NOT NULL AUTO_INCREMENT,
        `ProjectId` bigint NOT NULL,
        `Name` varchar(200) CHARACTER SET utf8mb4 NOT NULL,
        `Type` varchar(20) CHARACTER SET utf8mb4 NOT NULL,
        `FileUrl` varchar(500) CHARACTER SET utf8mb4 NULL,
        `FileSize` varchar(20) CHARACTER SET utf8mb4 NULL,
        `UploadDate` datetime(6) NOT NULL,
        `AppCode` varchar(255) CHARACTER SET utf8mb4 NULL,
        CONSTRAINT `PK_contracts` PRIMARY KEY (`Id`),
        CONSTRAINT `FK_contracts_projects_ProjectId` FOREIGN KEY (`ProjectId`) REFERENCES `projects` (`Id`) ON DELETE CASCADE
    ) CHARACTER SET=utf8mb4;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260330025826_Initial') THEN

    CREATE TABLE `devices` (
        `Id` bigint NOT NULL AUTO_INCREMENT,
        `AppCode` varchar(50) CHARACTER SET utf8mb4 NOT NULL,
        `Name` varchar(200) CHARACTER SET utf8mb4 NOT NULL,
        `Model` varchar(100) CHARACTER SET utf8mb4 NULL,
        `SerialNumber` varchar(100) CHARACTER SET utf8mb4 NULL,
        `Category` varchar(100) CHARACTER SET utf8mb4 NULL,
        `Location` varchar(200) CHARACTER SET utf8mb4 NULL,
        `AreaId` bigint NULL,
        `ProjectId` bigint NULL,
        `ProjectName` varchar(200) CHARACTER SET utf8mb4 NULL,
        `EnergyTypes` json NULL,
        `Status` varchar(20) CHARACTER SET utf8mb4 NOT NULL,
        `InstallDate` datetime(6) NULL,
        `LastMaintenance` datetime(6) NULL,
        `Supplier` varchar(200) CHARACTER SET utf8mb4 NULL,
        `WarrantyDate` datetime(6) NULL,
        `Power` double NULL,
        `Voltage` varchar(20) CHARACTER SET utf8mb4 NULL,
        `MeterInstalled` tinyint(1) NOT NULL,
        `CreatedAt` datetime(6) NOT NULL,
        `UpdatedAt` datetime(6) NOT NULL,
        CONSTRAINT `PK_devices` PRIMARY KEY (`Id`),
        CONSTRAINT `FK_devices_areas_AreaId` FOREIGN KEY (`AreaId`) REFERENCES `areas` (`Id`),
        CONSTRAINT `FK_devices_projects_ProjectId` FOREIGN KEY (`ProjectId`) REFERENCES `projects` (`Id`)
    ) CHARACTER SET=utf8mb4;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260330025826_Initial') THEN

    CREATE TABLE `work_summaries` (
        `Id` bigint NOT NULL AUTO_INCREMENT,
        `ProjectId` bigint NOT NULL,
        `FeedbackPerson` varchar(100) CHARACTER SET utf8mb4 NULL,
        `Assignee` varchar(100) CHARACTER SET utf8mb4 NULL,
        `Assistant` varchar(100) CHARACTER SET utf8mb4 NULL,
        `WorkContent` varchar(2000) CHARACTER SET utf8mb4 NULL,
        `Date` datetime(6) NOT NULL,
        `AppCode` varchar(255) CHARACTER SET utf8mb4 NULL,
        CONSTRAINT `PK_work_summaries` PRIMARY KEY (`Id`),
        CONSTRAINT `FK_work_summaries_projects_ProjectId` FOREIGN KEY (`ProjectId`) REFERENCES `projects` (`Id`) ON DELETE CASCADE
    ) CHARACTER SET=utf8mb4;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260330025826_Initial') THEN

    CREATE TABLE `air_quality_data` (
        `Id` bigint NOT NULL AUTO_INCREMENT,
        `DeviceId` bigint NULL,
        `AreaId` bigint NULL,
        `AreaName` varchar(200) CHARACTER SET utf8mb4 NULL,
        `FreshAirVolume` double NULL,
        `ExhaustVolume` double NULL,
        `SmokeConcentration` double NULL,
        `PM25` double NULL,
        `Temperature` double NULL,
        `Humidity` double NULL,
        `CO2` double NULL,
        `OilFume` double NULL,
        `RecordTime` datetime(6) NOT NULL,
        `Timestamp` datetime(6) NOT NULL,
        `AppCode` varchar(255) CHARACTER SET utf8mb4 NULL,
        CONSTRAINT `PK_air_quality_data` PRIMARY KEY (`Id`),
        CONSTRAINT `FK_air_quality_data_areas_AreaId` FOREIGN KEY (`AreaId`) REFERENCES `areas` (`Id`),
        CONSTRAINT `FK_air_quality_data_devices_DeviceId` FOREIGN KEY (`DeviceId`) REFERENCES `devices` (`Id`)
    ) CHARACTER SET=utf8mb4;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260330025826_Initial') THEN

    CREATE TABLE `alert_records` (
        `Id` bigint NOT NULL AUTO_INCREMENT,
        `AlertNo` varchar(50) CHARACTER SET utf8mb4 NOT NULL,
        `DeviceName` varchar(200) CHARACTER SET utf8mb4 NOT NULL,
        `DeviceCode` varchar(50) CHARACTER SET utf8mb4 NULL,
        `AreaName` varchar(200) CHARACTER SET utf8mb4 NULL,
        `AlertType` varchar(50) CHARACTER SET utf8mb4 NOT NULL,
        `Level` varchar(20) CHARACTER SET utf8mb4 NOT NULL,
        `Value` double NULL,
        `Threshold` double NULL,
        `AlertTime` datetime(6) NOT NULL,
        `Status` varchar(20) CHARACTER SET utf8mb4 NOT NULL,
        `Assignee` varchar(100) CHARACTER SET utf8mb4 NULL,
        `ResolveTime` datetime(6) NULL,
        `Remark` varchar(1000) CHARACTER SET utf8mb4 NULL,
        `DeviceId` bigint NULL,
        `AreaId` bigint NULL,
        `AppCode` varchar(255) CHARACTER SET utf8mb4 NULL,
        `CreatedAt` datetime(6) NOT NULL,
        `UpdatedAt` datetime(6) NOT NULL,
        CONSTRAINT `PK_alert_records` PRIMARY KEY (`Id`),
        CONSTRAINT `FK_alert_records_areas_AreaId` FOREIGN KEY (`AreaId`) REFERENCES `areas` (`Id`),
        CONSTRAINT `FK_alert_records_devices_DeviceId` FOREIGN KEY (`DeviceId`) REFERENCES `devices` (`Id`)
    ) CHARACTER SET=utf8mb4;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260330025826_Initial') THEN

    CREATE TABLE `archive_device_markers` (
        `Id` bigint NOT NULL AUTO_INCREMENT,
        `ArchiveId` bigint NOT NULL,
        `DeviceId` bigint NULL,
        `Name` varchar(200) CHARACTER SET utf8mb4 NOT NULL,
        `DeviceType` varchar(100) CHARACTER SET utf8mb4 NULL,
        `Model` varchar(100) CHARACTER SET utf8mb4 NULL,
        `X` double NOT NULL,
        `Y` double NOT NULL,
        `Z` double NOT NULL,
        `Sensors` longtext CHARACTER SET utf8mb4 NULL,
        `CreatedAt` datetime(6) NOT NULL,
        `UpdatedAt` datetime(6) NOT NULL,
        CONSTRAINT `PK_archive_device_markers` PRIMARY KEY (`Id`),
        CONSTRAINT `FK_archive_device_markers_archives_ArchiveId` FOREIGN KEY (`ArchiveId`) REFERENCES `archives` (`Id`) ON DELETE CASCADE,
        CONSTRAINT `FK_archive_device_markers_devices_DeviceId` FOREIGN KEY (`DeviceId`) REFERENCES `devices` (`Id`)
    ) CHARACTER SET=utf8mb4;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260330025826_Initial') THEN

    CREATE TABLE `area_devices` (
        `Id` bigint NOT NULL AUTO_INCREMENT,
        `AreaId` bigint NOT NULL,
        `DeviceId` bigint NOT NULL,
        `Name` varchar(200) CHARACTER SET utf8mb4 NOT NULL,
        `DeviceType` varchar(100) CHARACTER SET utf8mb4 NULL,
        `Status` varchar(20) CHARACTER SET utf8mb4 NOT NULL,
        `X` double NOT NULL,
        `Y` double NOT NULL,
        `AppCode` longtext CHARACTER SET utf8mb4 NULL,
        CONSTRAINT `PK_area_devices` PRIMARY KEY (`Id`),
        CONSTRAINT `FK_area_devices_areas_AreaId` FOREIGN KEY (`AreaId`) REFERENCES `areas` (`Id`) ON DELETE CASCADE,
        CONSTRAINT `FK_area_devices_devices_DeviceId` FOREIGN KEY (`DeviceId`) REFERENCES `devices` (`Id`) ON DELETE CASCADE
    ) CHARACTER SET=utf8mb4;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260330025826_Initial') THEN

    CREATE TABLE `data_rules` (
        `Id` bigint NOT NULL AUTO_INCREMENT,
        `Name` varchar(100) CHARACTER SET utf8mb4 NOT NULL,
        `RuleType` varchar(50) CHARACTER SET utf8mb4 NULL,
        `DataType` varchar(50) CHARACTER SET utf8mb4 NULL,
        `MinValue` double NULL,
        `MaxValue` double NULL,
        `Level` varchar(20) CHARACTER SET utf8mb4 NULL,
        `Priority` int NOT NULL,
        `IsActive` tinyint(1) NOT NULL,
        `DeviceId` bigint NULL,
        `AreaId` bigint NULL,
        `AppCode` varchar(255) CHARACTER SET utf8mb4 NULL,
        `RuleExpression` varchar(1000) CHARACTER SET utf8mb4 NULL,
        `Description` varchar(1000) CHARACTER SET utf8mb4 NULL,
        `CreatedAt` datetime(6) NOT NULL,
        `UpdatedAt` datetime(6) NOT NULL,
        CONSTRAINT `PK_data_rules` PRIMARY KEY (`Id`),
        CONSTRAINT `FK_data_rules_areas_AreaId` FOREIGN KEY (`AreaId`) REFERENCES `areas` (`Id`),
        CONSTRAINT `FK_data_rules_devices_DeviceId` FOREIGN KEY (`DeviceId`) REFERENCES `devices` (`Id`)
    ) CHARACTER SET=utf8mb4;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260330025826_Initial') THEN

    CREATE TABLE `device_data_records` (
        `Id` bigint NOT NULL AUTO_INCREMENT,
        `DeviceId` bigint NOT NULL,
        `SensorData` longtext CHARACTER SET utf8mb4 NULL,
        `Temperature` double NULL,
        `Humidity` double NULL,
        `PM25` double NULL,
        `PM10` double NULL,
        `CO2` double NULL,
        `CO` double NULL,
        `FreshAirVolume` double NULL,
        `CombustibleGas` double NULL,
        `Formaldehyde` double NULL,
        `Smoke` double NULL,
        `TVOC` double NULL,
        `ExhaustVolume` double NULL,
        `SmokeConcentration` double NULL,
        `OilFume` double NULL,
        `Noise` double NULL,
        `AreaId` bigint NULL,
        `AreaName` longtext CHARACTER SET utf8mb4 NULL,
        `DeviceName` longtext CHARACTER SET utf8mb4 NULL,
        `Status` longtext CHARACTER SET utf8mb4 NULL,
        `Level` longtext CHARACTER SET utf8mb4 NULL,
        `RecordTime` datetime(6) NOT NULL,
        `Timestamp` datetime(6) NOT NULL,
        `AppCode` longtext CHARACTER SET utf8mb4 NULL,
        CONSTRAINT `PK_device_data_records` PRIMARY KEY (`Id`),
        CONSTRAINT `FK_device_data_records_areas_AreaId` FOREIGN KEY (`AreaId`) REFERENCES `areas` (`Id`),
        CONSTRAINT `FK_device_data_records_devices_DeviceId` FOREIGN KEY (`DeviceId`) REFERENCES `devices` (`Id`) ON DELETE CASCADE
    ) CHARACTER SET=utf8mb4;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260330025826_Initial') THEN

    CREATE TABLE `device_sensors` (
        `Id` bigint NOT NULL AUTO_INCREMENT,
        `DeviceId` bigint NOT NULL,
        `Name` varchar(100) CHARACTER SET utf8mb4 NOT NULL,
        `SensorType` varchar(50) CHARACTER SET utf8mb4 NULL,
        `LastValue` varchar(50) CHARACTER SET utf8mb4 NULL,
        `Unit` varchar(20) CHARACTER SET utf8mb4 NULL,
        `AppCode` longtext CHARACTER SET utf8mb4 NULL,
        CONSTRAINT `PK_device_sensors` PRIMARY KEY (`Id`),
        CONSTRAINT `FK_device_sensors_devices_DeviceId` FOREIGN KEY (`DeviceId`) REFERENCES `devices` (`Id`) ON DELETE CASCADE
    ) CHARACTER SET=utf8mb4;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260330025826_Initial') THEN

    CREATE TABLE `environment_data` (
        `Id` bigint NOT NULL AUTO_INCREMENT,
        `DeviceId` bigint NOT NULL,
        `DeviceName` varchar(200) CHARACTER SET utf8mb4 NULL,
        `AreaId` bigint NULL,
        `AreaName` varchar(200) CHARACTER SET utf8mb4 NULL,
        `PM25` double NULL,
        `PM10` double NULL,
        `CO2` double NULL,
        `CO` double NULL,
        `Temperature` double NULL,
        `Humidity` double NULL,
        `Noise` double NULL,
        `CombustibleGas` double NULL,
        `Formaldehyde` double NULL,
        `Smoke` double NULL,
        `TVOC` double NULL,
        `RecordTime` datetime(6) NOT NULL,
        `Timestamp` datetime(6) NOT NULL,
        `AppCode` varchar(255) CHARACTER SET utf8mb4 NULL,
        CONSTRAINT `PK_environment_data` PRIMARY KEY (`Id`),
        CONSTRAINT `FK_environment_data_areas_AreaId` FOREIGN KEY (`AreaId`) REFERENCES `areas` (`Id`),
        CONSTRAINT `FK_environment_data_devices_DeviceId` FOREIGN KEY (`DeviceId`) REFERENCES `devices` (`Id`) ON DELETE CASCADE
    ) CHARACTER SET=utf8mb4;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260330025826_Initial') THEN

    CREATE TABLE `alert_process_logs` (
        `Id` bigint NOT NULL AUTO_INCREMENT,
        `AlertRecordId` bigint NULL,
        `ProcessedBy` varchar(100) CHARACTER SET utf8mb4 NULL,
        `Action` varchar(50) CHARACTER SET utf8mb4 NOT NULL,
        `Comment` varchar(2000) CHARACTER SET utf8mb4 NULL,
        `CreatedAt` datetime(6) NOT NULL,
        CONSTRAINT `PK_alert_process_logs` PRIMARY KEY (`Id`),
        CONSTRAINT `FK_alert_process_logs_alert_records_AlertRecordId` FOREIGN KEY (`AlertRecordId`) REFERENCES `alert_records` (`Id`) ON DELETE CASCADE
    ) CHARACTER SET=utf8mb4;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260330025826_Initial') THEN

    CREATE TABLE `work_orders` (
        `Id` bigint NOT NULL AUTO_INCREMENT,
        `OrderNo` varchar(50) CHARACTER SET utf8mb4 NOT NULL,
        `Title` varchar(200) CHARACTER SET utf8mb4 NOT NULL,
        `Type` varchar(50) CHARACTER SET utf8mb4 NOT NULL,
        `Priority` varchar(20) CHARACTER SET utf8mb4 NOT NULL,
        `Status` varchar(20) CHARACTER SET utf8mb4 NOT NULL,
        `CustomerId` bigint NULL,
        `DeviceId` bigint NULL,
        `AreaId` bigint NULL,
        `DeviceName` varchar(200) CHARACTER SET utf8mb4 NULL,
        `DeviceCode` varchar(50) CHARACTER SET utf8mb4 NULL,
        `AreaName` varchar(200) CHARACTER SET utf8mb4 NULL,
        `Description` varchar(2000) CHARACTER SET utf8mb4 NULL,
        `Reporter` varchar(100) CHARACTER SET utf8mb4 NULL,
        `ReportTime` datetime(6) NOT NULL,
        `Assignee` varchar(100) CHARACTER SET utf8mb4 NULL,
        `AssignTime` datetime(6) NULL,
        `EstimatedTime` datetime(6) NULL,
        `ResolvedTime` datetime(6) NULL,
        `ClosedTime` datetime(6) NULL,
        `ResolveDescription` varchar(2000) CHARACTER SET utf8mb4 NULL,
        `ProjectName` varchar(100) CHARACTER SET utf8mb4 NULL,
        `CreatedBy` varchar(100) CHARACTER SET utf8mb4 NULL,
        `AppCode` varchar(255) CHARACTER SET utf8mb4 NULL,
        `CreatedAt` datetime(6) NOT NULL,
        `UpdatedAt` datetime(6) NOT NULL,
        `AlertRecordId` bigint NULL,
        CONSTRAINT `PK_work_orders` PRIMARY KEY (`Id`),
        CONSTRAINT `FK_work_orders_alert_records_AlertRecordId` FOREIGN KEY (`AlertRecordId`) REFERENCES `alert_records` (`Id`),
        CONSTRAINT `FK_work_orders_areas_AreaId` FOREIGN KEY (`AreaId`) REFERENCES `areas` (`Id`),
        CONSTRAINT `FK_work_orders_customers_CustomerId` FOREIGN KEY (`CustomerId`) REFERENCES `customers` (`Id`),
        CONSTRAINT `FK_work_orders_devices_DeviceId` FOREIGN KEY (`DeviceId`) REFERENCES `devices` (`Id`)
    ) CHARACTER SET=utf8mb4;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260330025826_Initial') THEN

    CREATE TABLE `etl_tasks` (
        `Id` bigint NOT NULL AUTO_INCREMENT,
        `Name` varchar(100) CHARACTER SET utf8mb4 NOT NULL,
        `TaskType` varchar(50) CHARACTER SET utf8mb4 NULL,
        `TriggerType` varchar(50) CHARACTER SET utf8mb4 NULL,
        `SourceConfig` varchar(1000) CHARACTER SET utf8mb4 NULL,
        `TargetConfig` varchar(1000) CHARACTER SET utf8mb4 NULL,
        `TransformRule` varchar(1000) CHARACTER SET utf8mb4 NULL,
        `Schedule` varchar(20) CHARACTER SET utf8mb4 NULL,
        `Status` varchar(20) CHARACTER SET utf8mb4 NULL,
        `LastRunTime` datetime(6) NULL,
        `NextRunTime` datetime(6) NULL,
        `CompletedAt` datetime(6) NULL,
        `IsActive` tinyint(1) NOT NULL,
        `LastRunAt` varchar(20) CHARACTER SET utf8mb4 NULL,
        `NextRunAt` varchar(20) CHARACTER SET utf8mb4 NULL,
        `AppCode` varchar(255) CHARACTER SET utf8mb4 NULL,
        `DataRuleId` bigint NULL,
        `Description` varchar(1000) CHARACTER SET utf8mb4 NULL,
        `CreatedAt` datetime(6) NOT NULL,
        `UpdatedAt` datetime(6) NOT NULL,
        CONSTRAINT `PK_etl_tasks` PRIMARY KEY (`Id`),
        CONSTRAINT `FK_etl_tasks_data_rules_DataRuleId` FOREIGN KEY (`DataRuleId`) REFERENCES `data_rules` (`Id`)
    ) CHARACTER SET=utf8mb4;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260330025826_Initial') THEN

    CREATE TABLE `work_order_attachments` (
        `Id` bigint NOT NULL AUTO_INCREMENT,
        `WorkOrderId` bigint NOT NULL,
        `FileName` varchar(200) CHARACTER SET utf8mb4 NOT NULL,
        `FileUrl` varchar(500) CHARACTER SET utf8mb4 NULL,
        `FileSize` varchar(20) CHARACTER SET utf8mb4 NULL,
        `FileType` varchar(50) CHARACTER SET utf8mb4 NULL,
        `FileSizeBytes` bigint NOT NULL,
        `CreatedAt` datetime(6) NOT NULL,
        CONSTRAINT `PK_work_order_attachments` PRIMARY KEY (`Id`),
        CONSTRAINT `FK_work_order_attachments_work_orders_WorkOrderId` FOREIGN KEY (`WorkOrderId`) REFERENCES `work_orders` (`Id`) ON DELETE CASCADE
    ) CHARACTER SET=utf8mb4;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260330025826_Initial') THEN

    CREATE TABLE `work_order_logs` (
        `Id` bigint NOT NULL AUTO_INCREMENT,
        `WorkOrderId` bigint NOT NULL,
        `Operator` varchar(100) CHARACTER SET utf8mb4 NULL,
        `OperatorName` varchar(100) CHARACTER SET utf8mb4 NULL,
        `Action` varchar(50) CHARACTER SET utf8mb4 NOT NULL,
        `Comment` varchar(2000) CHARACTER SET utf8mb4 NULL,
        `CreatedAt` datetime(6) NOT NULL,
        CONSTRAINT `PK_work_order_logs` PRIMARY KEY (`Id`),
        CONSTRAINT `FK_work_order_logs_work_orders_WorkOrderId` FOREIGN KEY (`WorkOrderId`) REFERENCES `work_orders` (`Id`) ON DELETE CASCADE
    ) CHARACTER SET=utf8mb4;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260330025826_Initial') THEN

    CREATE INDEX `IX_air_quality_data_AppCode` ON `air_quality_data` (`AppCode`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260330025826_Initial') THEN

    CREATE INDEX `IX_air_quality_data_AreaId` ON `air_quality_data` (`AreaId`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260330025826_Initial') THEN

    CREATE INDEX `IX_air_quality_data_DeviceId` ON `air_quality_data` (`DeviceId`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260330025826_Initial') THEN

    CREATE INDEX `IX_air_quality_data_RecordTime` ON `air_quality_data` (`RecordTime`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260330025826_Initial') THEN

    CREATE INDEX `IX_alert_process_logs_AlertRecordId` ON `alert_process_logs` (`AlertRecordId`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260330025826_Initial') THEN

    CREATE INDEX `IX_alert_process_logs_ProcessedBy` ON `alert_process_logs` (`ProcessedBy`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260330025826_Initial') THEN

    CREATE UNIQUE INDEX `IX_alert_records_AlertNo` ON `alert_records` (`AlertNo`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260330025826_Initial') THEN

    CREATE INDEX `IX_alert_records_AlertTime` ON `alert_records` (`AlertTime`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260330025826_Initial') THEN

    CREATE INDEX `IX_alert_records_AlertType` ON `alert_records` (`AlertType`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260330025826_Initial') THEN

    CREATE INDEX `IX_alert_records_AppCode` ON `alert_records` (`AppCode`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260330025826_Initial') THEN

    CREATE INDEX `IX_alert_records_AreaId` ON `alert_records` (`AreaId`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260330025826_Initial') THEN

    CREATE INDEX `IX_alert_records_DeviceId` ON `alert_records` (`DeviceId`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260330025826_Initial') THEN

    CREATE INDEX `IX_alert_records_Level` ON `alert_records` (`Level`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260330025826_Initial') THEN

    CREATE INDEX `IX_alert_records_Status` ON `alert_records` (`Status`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260330025826_Initial') THEN

    CREATE INDEX `IX_archive_device_markers_ArchiveId` ON `archive_device_markers` (`ArchiveId`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260330025826_Initial') THEN

    CREATE INDEX `IX_archive_device_markers_DeviceId` ON `archive_device_markers` (`DeviceId`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260330025826_Initial') THEN

    CREATE INDEX `IX_archives_AppCode` ON `archives` (`AppCode`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260330025826_Initial') THEN

    CREATE INDEX `IX_archives_AreaId` ON `archives` (`AreaId`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260330025826_Initial') THEN

    CREATE INDEX `IX_archives_ProjectId` ON `archives` (`ProjectId`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260330025826_Initial') THEN

    CREATE INDEX `IX_archives_Type` ON `archives` (`Type`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260330025826_Initial') THEN

    CREATE INDEX `IX_area_devices_AreaId` ON `area_devices` (`AreaId`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260330025826_Initial') THEN

    CREATE INDEX `IX_area_devices_DeviceId` ON `area_devices` (`DeviceId`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260330025826_Initial') THEN

    CREATE INDEX `IX_areas_AppCode` ON `areas` (`AppCode`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260330025826_Initial') THEN

    CREATE INDEX `IX_areas_CustomerId` ON `areas` (`CustomerId`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260330025826_Initial') THEN

    CREATE INDEX `IX_areas_ParentId` ON `areas` (`ParentId`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260330025826_Initial') THEN

    CREATE INDEX `IX_areas_Type` ON `areas` (`Type`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260330025826_Initial') THEN

    CREATE INDEX `IX_cameras_AppCode` ON `cameras` (`AppCode`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260330025826_Initial') THEN

    CREATE INDEX `IX_cameras_AreaId` ON `cameras` (`AreaId`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260330025826_Initial') THEN

    CREATE INDEX `IX_contracts_AppCode` ON `contracts` (`AppCode`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260330025826_Initial') THEN

    CREATE INDEX `IX_contracts_ProjectId` ON `contracts` (`ProjectId`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260330025826_Initial') THEN

    CREATE UNIQUE INDEX `IX_customers_AppCode` ON `customers` (`AppCode`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260330025826_Initial') THEN

    CREATE UNIQUE INDEX `IX_customers_Code` ON `customers` (`Code`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260330025826_Initial') THEN

    CREATE INDEX `IX_customers_Status` ON `customers` (`Status`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260330025826_Initial') THEN

    CREATE INDEX `IX_data_rules_AppCode` ON `data_rules` (`AppCode`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260330025826_Initial') THEN

    CREATE INDEX `IX_data_rules_AreaId` ON `data_rules` (`AreaId`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260330025826_Initial') THEN

    CREATE INDEX `IX_data_rules_DeviceId` ON `data_rules` (`DeviceId`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260330025826_Initial') THEN

    CREATE INDEX `IX_data_rules_IsActive` ON `data_rules` (`IsActive`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260330025826_Initial') THEN

    CREATE INDEX `IX_data_rules_RuleType` ON `data_rules` (`RuleType`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260330025826_Initial') THEN

    CREATE INDEX `IX_device_data_records_AreaId` ON `device_data_records` (`AreaId`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260330025826_Initial') THEN

    CREATE INDEX `IX_device_data_records_DeviceId` ON `device_data_records` (`DeviceId`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260330025826_Initial') THEN

    CREATE INDEX `IX_device_data_records_RecordTime` ON `device_data_records` (`RecordTime`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260330025826_Initial') THEN

    CREATE INDEX `IX_device_sensors_DeviceId` ON `device_sensors` (`DeviceId`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260330025826_Initial') THEN

    CREATE INDEX `IX_devices_AppCode` ON `devices` (`AppCode`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260330025826_Initial') THEN

    CREATE INDEX `IX_devices_AreaId` ON `devices` (`AreaId`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260330025826_Initial') THEN

    CREATE INDEX `IX_devices_ProjectId` ON `devices` (`ProjectId`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260330025826_Initial') THEN

    CREATE INDEX `IX_devices_SerialNumber` ON `devices` (`SerialNumber`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260330025826_Initial') THEN

    CREATE INDEX `IX_devices_Status` ON `devices` (`Status`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260330025826_Initial') THEN

    CREATE INDEX `IX_dictionary_items_AppCode` ON `dictionary_items` (`AppCode`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260330025826_Initial') THEN

    CREATE INDEX `IX_dictionary_items_Code` ON `dictionary_items` (`Code`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260330025826_Initial') THEN

    CREATE INDEX `IX_dictionary_items_DictionaryTypeConfigId` ON `dictionary_items` (`DictionaryTypeConfigId`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260330025826_Initial') THEN

    CREATE INDEX `IX_dictionary_items_TypeCode` ON `dictionary_items` (`TypeCode`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260330025826_Initial') THEN

    CREATE INDEX `IX_dictionary_types_AppCode` ON `dictionary_types` (`AppCode`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260330025826_Initial') THEN

    CREATE UNIQUE INDEX `IX_dictionary_types_Code` ON `dictionary_types` (`Code`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260330025826_Initial') THEN

    CREATE INDEX `IX_dictionary_types_IsActive` ON `dictionary_types` (`IsActive`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260330025826_Initial') THEN

    CREATE INDEX `IX_environment_data_AppCode` ON `environment_data` (`AppCode`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260330025826_Initial') THEN

    CREATE INDEX `IX_environment_data_AreaId` ON `environment_data` (`AreaId`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260330025826_Initial') THEN

    CREATE INDEX `IX_environment_data_DeviceId` ON `environment_data` (`DeviceId`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260330025826_Initial') THEN

    CREATE INDEX `IX_environment_data_RecordTime` ON `environment_data` (`RecordTime`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260330025826_Initial') THEN

    CREATE INDEX `IX_etl_tasks_AppCode` ON `etl_tasks` (`AppCode`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260330025826_Initial') THEN

    CREATE INDEX `IX_etl_tasks_DataRuleId` ON `etl_tasks` (`DataRuleId`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260330025826_Initial') THEN

    CREATE INDEX `IX_etl_tasks_Status` ON `etl_tasks` (`Status`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260330025826_Initial') THEN

    CREATE INDEX `IX_etl_tasks_TaskType` ON `etl_tasks` (`TaskType`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260330025826_Initial') THEN

    CREATE INDEX `IX_login_logs_Email` ON `login_logs` (`Email`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260330025826_Initial') THEN

    CREATE INDEX `IX_login_logs_LoginTime` ON `login_logs` (`LoginTime`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260330025826_Initial') THEN

    CREATE INDEX `IX_login_logs_UserId` ON `login_logs` (`UserId`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260330025826_Initial') THEN

    CREATE INDEX `IX_operation_logs_Module` ON `operation_logs` (`Module`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260330025826_Initial') THEN

    CREATE INDEX `IX_operation_logs_Operation` ON `operation_logs` (`Operation`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260330025826_Initial') THEN

    CREATE INDEX `IX_operation_logs_OperationTime` ON `operation_logs` (`OperationTime`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260330025826_Initial') THEN

    CREATE INDEX `IX_operation_logs_UserId` ON `operation_logs` (`UserId`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260330025826_Initial') THEN

    CREATE INDEX `IX_projects_AppCode` ON `projects` (`AppCode`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260330025826_Initial') THEN

    CREATE INDEX `IX_projects_CustomerId` ON `projects` (`CustomerId`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260330025826_Initial') THEN

    CREATE INDEX `IX_projects_Status` ON `projects` (`Status`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260330025826_Initial') THEN

    CREATE INDEX `IX_protocol_configs_AppCode` ON `protocol_configs` (`AppCode`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260330025826_Initial') THEN

    CREATE INDEX `IX_protocol_configs_IsActive` ON `protocol_configs` (`IsActive`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260330025826_Initial') THEN

    CREATE INDEX `IX_protocol_configs_ProtocolType` ON `protocol_configs` (`ProtocolType`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260330025826_Initial') THEN

    CREATE INDEX `IX_roles_AppCode` ON `roles` (`AppCode`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260330025826_Initial') THEN

    CREATE UNIQUE INDEX `IX_roles_Code` ON `roles` (`Code`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260330025826_Initial') THEN

    CREATE INDEX `IX_system_settings_AppCode` ON `system_settings` (`AppCode`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260330025826_Initial') THEN

    CREATE INDEX `IX_system_settings_Category` ON `system_settings` (`Category`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260330025826_Initial') THEN

    CREATE UNIQUE INDEX `IX_system_settings_Key` ON `system_settings` (`Key`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260330025826_Initial') THEN

    CREATE INDEX `IX_users_AppCode` ON `users` (`AppCode`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260330025826_Initial') THEN

    CREATE INDEX `IX_users_CustomerId` ON `users` (`CustomerId`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260330025826_Initial') THEN

    CREATE UNIQUE INDEX `IX_users_Email` ON `users` (`Email`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260330025826_Initial') THEN

    CREATE INDEX `IX_users_IsActive` ON `users` (`IsActive`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260330025826_Initial') THEN

    CREATE INDEX `IX_users_Role` ON `users` (`Role`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260330025826_Initial') THEN

    CREATE INDEX `IX_users_RoleId` ON `users` (`RoleId`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260330025826_Initial') THEN

    CREATE INDEX `IX_work_order_attachments_WorkOrderId` ON `work_order_attachments` (`WorkOrderId`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260330025826_Initial') THEN

    CREATE INDEX `IX_work_order_logs_WorkOrderId` ON `work_order_logs` (`WorkOrderId`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260330025826_Initial') THEN

    CREATE INDEX `IX_work_orders_AlertRecordId` ON `work_orders` (`AlertRecordId`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260330025826_Initial') THEN

    CREATE INDEX `IX_work_orders_AppCode` ON `work_orders` (`AppCode`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260330025826_Initial') THEN

    CREATE INDEX `IX_work_orders_AreaId` ON `work_orders` (`AreaId`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260330025826_Initial') THEN

    CREATE INDEX `IX_work_orders_CustomerId` ON `work_orders` (`CustomerId`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260330025826_Initial') THEN

    CREATE INDEX `IX_work_orders_DeviceId` ON `work_orders` (`DeviceId`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260330025826_Initial') THEN

    CREATE UNIQUE INDEX `IX_work_orders_OrderNo` ON `work_orders` (`OrderNo`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260330025826_Initial') THEN

    CREATE INDEX `IX_work_orders_Priority` ON `work_orders` (`Priority`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260330025826_Initial') THEN

    CREATE INDEX `IX_work_orders_ReportTime` ON `work_orders` (`ReportTime`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260330025826_Initial') THEN

    CREATE INDEX `IX_work_orders_Status` ON `work_orders` (`Status`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260330025826_Initial') THEN

    CREATE INDEX `IX_work_orders_Type` ON `work_orders` (`Type`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260330025826_Initial') THEN

    CREATE INDEX `IX_work_summaries_AppCode` ON `work_summaries` (`AppCode`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260330025826_Initial') THEN

    CREATE INDEX `IX_work_summaries_Date` ON `work_summaries` (`Date`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260330025826_Initial') THEN

    CREATE INDEX `IX_work_summaries_ProjectId` ON `work_summaries` (`ProjectId`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260330025826_Initial') THEN

    INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
    VALUES ('20260330025826_Initial', '8.0.11');

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

COMMIT;

START TRANSACTION;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260430084917_newtable') THEN

    ALTER TABLE `system_settings` DROP INDEX `IX_system_settings_AppCode`;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260430084917_newtable') THEN

    ALTER TABLE `system_settings` DROP INDEX `IX_system_settings_Category`;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260430084917_newtable') THEN

    ALTER TABLE `system_settings` DROP INDEX `IX_system_settings_Key`;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260430084917_newtable') THEN

    ALTER TABLE `system_settings` MODIFY COLUMN `AppCode` longtext CHARACTER SET utf8mb4 NULL;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260430084917_newtable') THEN

    ALTER TABLE `protocol_configs` MODIFY COLUMN `AppCode` varchar(50) CHARACTER SET utf8mb4 NULL;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260430084917_newtable') THEN

    CREATE TABLE `attachments` (
        `Id` bigint NOT NULL AUTO_INCREMENT,
        `Module` varchar(50) CHARACTER SET utf8mb4 NOT NULL,
        `BusinessId` bigint NULL,
        `Name` varchar(200) CHARACTER SET utf8mb4 NOT NULL,
        `OriginalName` varchar(500) CHARACTER SET utf8mb4 NULL,
        `Extension` varchar(20) CHARACTER SET utf8mb4 NULL,
        `FileUrl` varchar(1000) CHARACTER SET utf8mb4 NULL,
        `FileSize` varchar(20) CHARACTER SET utf8mb4 NULL,
        `FileSizeBytes` bigint NOT NULL,
        `ContentType` varchar(100) CHARACTER SET utf8mb4 NULL,
        `UploadDate` datetime(6) NOT NULL,
        `UploadUserId` bigint NULL,
        `AppCode` varchar(255) CHARACTER SET utf8mb4 NULL,
        `Remark` varchar(500) CHARACTER SET utf8mb4 NULL,
        CONSTRAINT `PK_attachments` PRIMARY KEY (`Id`)
    ) CHARACTER SET=utf8mb4;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260430084917_newtable') THEN

    CREATE TABLE `device_commands` (
        `Id` bigint NOT NULL AUTO_INCREMENT,
        `CommandId` varchar(64) CHARACTER SET utf8mb4 NOT NULL,
        `AppCode` varchar(50) CHARACTER SET utf8mb4 NULL,
        `DeviceId` bigint NOT NULL,
        `SerialNumber` varchar(100) CHARACTER SET utf8mb4 NULL,
        `CommandType` varchar(50) CHARACTER SET utf8mb4 NOT NULL,
        `Parameters` longtext CHARACTER SET utf8mb4 NULL,
        `Status` int NOT NULL,
        `SentAt` datetime(6) NULL,
        `CompletedAt` datetime(6) NULL,
        `Result` varchar(500) CHARACTER SET utf8mb4 NULL,
        `ErrorMessage` varchar(1000) CHARACTER SET utf8mb4 NULL,
        `TimeoutSeconds` int NOT NULL,
        `RetryCount` int NOT NULL,
        `MaxRetries` int NOT NULL,
        `CreatedAt` datetime(6) NOT NULL,
        `UpdatedAt` datetime(6) NOT NULL,
        `CreatedBy` bigint NULL,
        `CreatedByName` varchar(100) CHARACTER SET utf8mb4 NULL,
        CONSTRAINT `PK_device_commands` PRIMARY KEY (`Id`),
        CONSTRAINT `FK_device_commands_devices_DeviceId` FOREIGN KEY (`DeviceId`) REFERENCES `devices` (`Id`) ON DELETE CASCADE
    ) CHARACTER SET=utf8mb4;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260430084917_newtable') THEN

    CREATE TABLE `command_histories` (
        `Id` bigint NOT NULL AUTO_INCREMENT,
        `AppCode` varchar(50) CHARACTER SET utf8mb4 NULL,
        `CommandId` bigint NOT NULL,
        `Type` int NOT NULL,
        `FromStatus` int NULL,
        `ToStatus` int NULL,
        `Description` varchar(500) CHARACTER SET utf8mb4 NULL,
        `Data` longtext CHARACTER SET utf8mb4 NULL,
        `OperatorId` bigint NULL,
        `OperatorName` varchar(100) CHARACTER SET utf8mb4 NULL,
        `CreatedAt` datetime(6) NOT NULL,
        CONSTRAINT `PK_command_histories` PRIMARY KEY (`Id`),
        CONSTRAINT `FK_command_histories_device_commands_CommandId` FOREIGN KEY (`CommandId`) REFERENCES `device_commands` (`Id`) ON DELETE CASCADE
    ) CHARACTER SET=utf8mb4;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260430084917_newtable') THEN

    CREATE INDEX `IX_attachments_AppCode` ON `attachments` (`AppCode`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260430084917_newtable') THEN

    CREATE INDEX `IX_attachments_BusinessId` ON `attachments` (`BusinessId`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260430084917_newtable') THEN

    CREATE INDEX `IX_attachments_Module` ON `attachments` (`Module`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260430084917_newtable') THEN

    CREATE INDEX `IX_attachments_UploadDate` ON `attachments` (`UploadDate`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260430084917_newtable') THEN

    CREATE INDEX `IX_command_histories_AppCode` ON `command_histories` (`AppCode`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260430084917_newtable') THEN

    CREATE INDEX `IX_command_histories_CommandId` ON `command_histories` (`CommandId`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260430084917_newtable') THEN

    CREATE INDEX `IX_command_histories_CreatedAt` ON `command_histories` (`CreatedAt`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260430084917_newtable') THEN

    CREATE INDEX `IX_device_commands_AppCode` ON `device_commands` (`AppCode`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260430084917_newtable') THEN

    CREATE UNIQUE INDEX `IX_device_commands_CommandId` ON `device_commands` (`CommandId`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260430084917_newtable') THEN

    CREATE INDEX `IX_device_commands_CreatedAt` ON `device_commands` (`CreatedAt`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260430084917_newtable') THEN

    CREATE INDEX `IX_device_commands_DeviceId` ON `device_commands` (`DeviceId`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260430084917_newtable') THEN

    CREATE INDEX `IX_device_commands_Status` ON `device_commands` (`Status`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260430084917_newtable') THEN

    INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
    VALUES ('20260430084917_newtable', '8.0.11');

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

COMMIT;

START TRANSACTION;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260517075650_AddControlledDevices') THEN

    CREATE TABLE `controlled_devices` (
        `Id` bigint NOT NULL AUTO_INCREMENT,
        `AppCode` varchar(50) CHARACTER SET utf8mb4 NULL,
        `DeviceId` bigint NOT NULL,
        `DeviceName` varchar(100) CHARACTER SET utf8mb4 NOT NULL,
        `SerialNumber` varchar(100) CHARACTER SET utf8mb4 NULL,
        `Model` varchar(100) CHARACTER SET utf8mb4 NULL,
        `Category` varchar(50) CHARACTER SET utf8mb4 NULL,
        `Location` varchar(200) CHARACTER SET utf8mb4 NULL,
        `Remark` varchar(500) CHARACTER SET utf8mb4 NULL,
        `Priority` int NOT NULL,
        `IsEnabled` tinyint(1) NOT NULL,
        `IsFavorite` tinyint(1) NOT NULL,
        `RegisteredAt` datetime(6) NOT NULL,
        `LastCommandAt` datetime(6) NULL,
        `CommandCount` int NOT NULL,
        `CreatedBy` bigint NULL,
        `CreatedByName` varchar(100) CHARACTER SET utf8mb4 NULL,
        `CreatedAt` datetime(6) NOT NULL,
        `UpdatedAt` datetime(6) NOT NULL,
        CONSTRAINT `PK_controlled_devices` PRIMARY KEY (`Id`),
        CONSTRAINT `FK_controlled_devices_devices_DeviceId` FOREIGN KEY (`DeviceId`) REFERENCES `devices` (`Id`) ON DELETE CASCADE
    ) CHARACTER SET=utf8mb4;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260517075650_AddControlledDevices') THEN

    CREATE INDEX `IX_controlled_devices_AppCode` ON `controlled_devices` (`AppCode`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260517075650_AddControlledDevices') THEN

    CREATE UNIQUE INDEX `IX_controlled_devices_DeviceId` ON `controlled_devices` (`DeviceId`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260517075650_AddControlledDevices') THEN

    CREATE INDEX `IX_controlled_devices_IsEnabled` ON `controlled_devices` (`IsEnabled`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260517075650_AddControlledDevices') THEN

    CREATE INDEX `IX_controlled_devices_IsFavorite` ON `controlled_devices` (`IsFavorite`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260517075650_AddControlledDevices') THEN

    CREATE INDEX `IX_controlled_devices_Priority` ON `controlled_devices` (`Priority`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260517075650_AddControlledDevices') THEN

    CREATE INDEX `IX_controlled_devices_RegisteredAt` ON `controlled_devices` (`RegisteredAt`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260517075650_AddControlledDevices') THEN

    INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
    VALUES ('20260517075650_AddControlledDevices', '8.0.11');

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

COMMIT;

START TRANSACTION;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260517094239_AddDataCollectionModules') THEN

    CREATE TABLE `database_configs` (
        `Id` bigint NOT NULL AUTO_INCREMENT,
        `Name` varchar(100) CHARACTER SET utf8mb4 NOT NULL,
        `DatabaseType` varchar(50) CHARACTER SET utf8mb4 NOT NULL,
        `Status` varchar(20) CHARACTER SET utf8mb4 NOT NULL,
        `IsActive` tinyint(1) NOT NULL,
        `Host` varchar(255) CHARACTER SET utf8mb4 NOT NULL,
        `Port` int NOT NULL,
        `DatabaseName` varchar(100) CHARACTER SET utf8mb4 NOT NULL,
        `Username` varchar(100) CHARACTER SET utf8mb4 NULL,
        `EncryptedPassword` longtext CHARACTER SET utf8mb4 NULL,
        `ConnectionString` longtext CHARACTER SET utf8mb4 NULL,
        `Config` longtext CHARACTER SET utf8mb4 NULL,
        `Description` varchar(500) CHARACTER SET utf8mb4 NULL,
        `LastTestAt` datetime(6) NULL,
        `AppCode` varchar(50) CHARACTER SET utf8mb4 NULL,
        `CreatedAt` datetime(6) NOT NULL,
        `UpdatedAt` datetime(6) NOT NULL,
        CONSTRAINT `PK_database_configs` PRIMARY KEY (`Id`)
    ) CHARACTER SET=utf8mb4;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260517094239_AddDataCollectionModules') THEN

    CREATE TABLE `gateways` (
        `Id` bigint NOT NULL AUTO_INCREMENT,
        `Name` varchar(100) CHARACTER SET utf8mb4 NOT NULL,
        `GatewayType` varchar(50) CHARACTER SET utf8mb4 NULL,
        `SourceProtocol` varchar(50) CHARACTER SET utf8mb4 NOT NULL,
        `TargetProtocol` varchar(50) CHARACTER SET utf8mb4 NOT NULL,
        `Status` varchar(20) CHARACTER SET utf8mb4 NOT NULL,
        `IsActive` tinyint(1) NOT NULL,
        `Throughput` int NOT NULL,
        `Config` longtext CHARACTER SET utf8mb4 NULL,
        `Description` varchar(500) CHARACTER SET utf8mb4 NULL,
        `AppCode` varchar(50) CHARACTER SET utf8mb4 NULL,
        `CreatedAt` datetime(6) NOT NULL,
        `UpdatedAt` datetime(6) NOT NULL,
        CONSTRAINT `PK_gateways` PRIMARY KEY (`Id`)
    ) CHARACTER SET=utf8mb4;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260517094239_AddDataCollectionModules') THEN

    CREATE TABLE `plugins` (
        `Id` bigint NOT NULL AUTO_INCREMENT,
        `Name` varchar(100) CHARACTER SET utf8mb4 NOT NULL,
        `Version` varchar(50) CHARACTER SET utf8mb4 NOT NULL,
        `Status` varchar(50) CHARACTER SET utf8mb4 NOT NULL,
        `IsActive` tinyint(1) NOT NULL,
        `PluginType` varchar(50) CHARACTER SET utf8mb4 NULL,
        `Description` varchar(500) CHARACTER SET utf8mb4 NULL,
        `Author` varchar(100) CHARACTER SET utf8mb4 NULL,
        `FilePath` varchar(500) CHARACTER SET utf8mb4 NULL,
        `Config` longtext CHARACTER SET utf8mb4 NULL,
        `Dependencies` longtext CHARACTER SET utf8mb4 NULL,
        `InstalledAt` datetime(6) NULL,
        `AppCode` varchar(50) CHARACTER SET utf8mb4 NULL,
        `CreatedAt` datetime(6) NOT NULL,
        `UpdatedAt` datetime(6) NOT NULL,
        CONSTRAINT `PK_plugins` PRIMARY KEY (`Id`)
    ) CHARACTER SET=utf8mb4;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260517094239_AddDataCollectionModules') THEN

    CREATE TABLE `tunnels` (
        `Id` bigint NOT NULL AUTO_INCREMENT,
        `Name` varchar(100) CHARACTER SET utf8mb4 NOT NULL,
        `TunnelType` varchar(20) CHARACTER SET utf8mb4 NOT NULL,
        `Status` varchar(20) CHARACTER SET utf8mb4 NOT NULL,
        `IsActive` tinyint(1) NOT NULL,
        `LocalPort` int NOT NULL,
        `RemotePort` int NOT NULL,
        `RemoteHost` varchar(255) CHARACTER SET utf8mb4 NULL,
        `Encryption` tinyint(1) NOT NULL,
        `Bandwidth` varchar(50) CHARACTER SET utf8mb4 NULL,
        `Config` longtext CHARACTER SET utf8mb4 NULL,
        `Description` varchar(500) CHARACTER SET utf8mb4 NULL,
        `AppCode` varchar(50) CHARACTER SET utf8mb4 NULL,
        `CreatedAt` datetime(6) NOT NULL,
        `UpdatedAt` datetime(6) NOT NULL,
        CONSTRAINT `PK_tunnels` PRIMARY KEY (`Id`)
    ) CHARACTER SET=utf8mb4;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260517094239_AddDataCollectionModules') THEN

    CREATE INDEX `IX_database_configs_AppCode` ON `database_configs` (`AppCode`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260517094239_AddDataCollectionModules') THEN

    CREATE INDEX `IX_database_configs_DatabaseType` ON `database_configs` (`DatabaseType`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260517094239_AddDataCollectionModules') THEN

    CREATE INDEX `IX_database_configs_Status` ON `database_configs` (`Status`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260517094239_AddDataCollectionModules') THEN

    CREATE INDEX `IX_gateways_AppCode` ON `gateways` (`AppCode`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260517094239_AddDataCollectionModules') THEN

    CREATE INDEX `IX_gateways_SourceProtocol` ON `gateways` (`SourceProtocol`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260517094239_AddDataCollectionModules') THEN

    CREATE INDEX `IX_gateways_Status` ON `gateways` (`Status`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260517094239_AddDataCollectionModules') THEN

    CREATE INDEX `IX_gateways_TargetProtocol` ON `gateways` (`TargetProtocol`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260517094239_AddDataCollectionModules') THEN

    CREATE INDEX `IX_plugins_AppCode` ON `plugins` (`AppCode`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260517094239_AddDataCollectionModules') THEN

    CREATE INDEX `IX_plugins_PluginType` ON `plugins` (`PluginType`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260517094239_AddDataCollectionModules') THEN

    CREATE INDEX `IX_plugins_Status` ON `plugins` (`Status`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260517094239_AddDataCollectionModules') THEN

    CREATE INDEX `IX_tunnels_AppCode` ON `tunnels` (`AppCode`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260517094239_AddDataCollectionModules') THEN

    CREATE INDEX `IX_tunnels_Status` ON `tunnels` (`Status`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260517094239_AddDataCollectionModules') THEN

    CREATE INDEX `IX_tunnels_TunnelType` ON `tunnels` (`TunnelType`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260517094239_AddDataCollectionModules') THEN

    INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
    VALUES ('20260517094239_AddDataCollectionModules', '8.0.11');

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

COMMIT;

START TRANSACTION;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260519055933_AddEnergyMeteringFields') THEN

    ALTER TABLE `device_data_records` ADD `ElectricKWh` double NULL;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260519055933_AddEnergyMeteringFields') THEN

    ALTER TABLE `device_data_records` ADD `ElectricPower` double NULL;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260519055933_AddEnergyMeteringFields') THEN

    ALTER TABLE `device_data_records` ADD `GasFlow` double NULL;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260519055933_AddEnergyMeteringFields') THEN

    ALTER TABLE `device_data_records` ADD `GasTotal` double NULL;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260519055933_AddEnergyMeteringFields') THEN

    ALTER TABLE `device_data_records` ADD `WaterFlow` double NULL;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260519055933_AddEnergyMeteringFields') THEN

    ALTER TABLE `device_data_records` ADD `WaterTotal` double NULL;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260519055933_AddEnergyMeteringFields') THEN

    INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
    VALUES ('20260519055933_AddEnergyMeteringFields', '8.0.11');

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

COMMIT;

START TRANSACTION;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260627085758_AddAnShengIntegration') THEN

    ALTER TABLE `devices` ADD `ProtocolConfigId` bigint NULL;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260627085758_AddAnShengIntegration') THEN

    CREATE TABLE `ansheng_device_configs` (
        `Id` bigint NOT NULL AUTO_INCREMENT,
        `DeviceId` bigint NOT NULL,
        `AppCode` varchar(50) CHARACTER SET utf8mb4 NULL,
        `Imei` varchar(50) CHARACTER SET utf8mb4 NOT NULL,
        `GetDevStatusSec` int NULL,
        `GetDevStatusQ` varchar(255) CHARACTER SET utf8mb4 NULL,
        `OrderUpSec` int NULL,
        `Rs485Sec` int NULL,
        `CreatedAt` datetime(6) NOT NULL,
        `UpdatedAt` datetime(6) NOT NULL,
        CONSTRAINT `PK_ansheng_device_configs` PRIMARY KEY (`Id`),
        CONSTRAINT `FK_ansheng_device_configs_devices_DeviceId` FOREIGN KEY (`DeviceId`) REFERENCES `devices` (`Id`) ON DELETE CASCADE
    ) CHARACTER SET=utf8mb4;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260627085758_AddAnShengIntegration') THEN

    CREATE TABLE `discovered_ansheng_devices` (
        `Id` bigint NOT NULL AUTO_INCREMENT,
        `AppCode` varchar(50) CHARACTER SET utf8mb4 NULL,
        `Imei` varchar(50) CHARACTER SET utf8mb4 NOT NULL,
        `Model` varchar(100) CHARACTER SET utf8mb4 NULL,
        `NetType` varchar(50) CHARACTER SET utf8mb4 NULL,
        `DiscoveredAt` datetime(6) NOT NULL,
        `LastSeenAt` datetime(6) NULL,
        `IsClaimed` tinyint(1) NOT NULL,
        `ClaimedDeviceId` bigint NULL,
        CONSTRAINT `PK_discovered_ansheng_devices` PRIMARY KEY (`Id`)
    ) CHARACTER SET=utf8mb4;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260627085758_AddAnShengIntegration') THEN

    CREATE INDEX `IX_devices_ProtocolConfigId` ON `devices` (`ProtocolConfigId`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260627085758_AddAnShengIntegration') THEN

    CREATE INDEX `IX_ansheng_device_configs_AppCode` ON `ansheng_device_configs` (`AppCode`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260627085758_AddAnShengIntegration') THEN

    CREATE UNIQUE INDEX `IX_ansheng_device_configs_DeviceId` ON `ansheng_device_configs` (`DeviceId`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260627085758_AddAnShengIntegration') THEN

    CREATE INDEX `IX_ansheng_device_configs_Imei` ON `ansheng_device_configs` (`Imei`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260627085758_AddAnShengIntegration') THEN

    CREATE INDEX `IX_discovered_ansheng_devices_AppCode` ON `discovered_ansheng_devices` (`AppCode`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260627085758_AddAnShengIntegration') THEN

    CREATE INDEX `IX_discovered_ansheng_devices_Imei` ON `discovered_ansheng_devices` (`Imei`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260627085758_AddAnShengIntegration') THEN

    CREATE INDEX `IX_discovered_ansheng_devices_IsClaimed` ON `discovered_ansheng_devices` (`IsClaimed`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260627085758_AddAnShengIntegration') THEN

    ALTER TABLE `devices` ADD CONSTRAINT `FK_devices_protocol_configs_ProtocolConfigId` FOREIGN KEY (`ProtocolConfigId`) REFERENCES `protocol_configs` (`Id`) ON DELETE SET NULL;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260627085758_AddAnShengIntegration') THEN

    INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
    VALUES ('20260627085758_AddAnShengIntegration', '8.0.11');

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

COMMIT;

START TRANSACTION;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260803122119_AddAnShengProfileAndDiscoveredColumns') THEN

    ALTER TABLE `discovered_ansheng_devices` ADD `Iccid` varchar(50) CHARACTER SET utf8mb4 NULL;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260803122119_AddAnShengProfileAndDiscoveredColumns') THEN

    ALTER TABLE `discovered_ansheng_devices` ADD `Kind` int NOT NULL DEFAULT 0;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260803122119_AddAnShengProfileAndDiscoveredColumns') THEN

    ALTER TABLE `discovered_ansheng_devices` ADD `LastProbedAt` datetime(6) NULL;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260803122119_AddAnShengProfileAndDiscoveredColumns') THEN

    ALTER TABLE `discovered_ansheng_devices` ADD `ProbeError` varchar(500) CHARACTER SET utf8mb4 NULL;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260803122119_AddAnShengProfileAndDiscoveredColumns') THEN

    ALTER TABLE `discovered_ansheng_devices` ADD `ProbeStatus` int NOT NULL DEFAULT 0;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260803122119_AddAnShengProfileAndDiscoveredColumns') THEN

    ALTER TABLE `discovered_ansheng_devices` ADD `SlotAmount` int NULL;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260803122119_AddAnShengProfileAndDiscoveredColumns') THEN

    ALTER TABLE `discovered_ansheng_devices` ADD `Version` varchar(100) CHARACTER SET utf8mb4 NULL;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260803122119_AddAnShengProfileAndDiscoveredColumns') THEN

    CREATE TABLE `ansheng_device_profiles` (
        `Id` bigint NOT NULL AUTO_INCREMENT,
        `AppCode` varchar(50) CHARACTER SET utf8mb4 NOT NULL,
        `Imei` varchar(50) CHARACTER SET utf8mb4 NOT NULL,
        `DeviceId` bigint NULL,
        `Kind` int NOT NULL,
        `KindSource` int NOT NULL,
        `NetType` varchar(50) CHARACTER SET utf8mb4 NULL,
        `SlotAmount` int NULL,
        `PhaseAmount` int NULL,
        `Version` varchar(100) CHARACTER SET utf8mb4 NULL,
        `Model` varchar(100) CHARACTER SET utf8mb4 NULL,
        `Iccid` varchar(50) CHARACTER SET utf8mb4 NULL,
        `Signal` int NULL,
        `ProbeStatus` int NOT NULL,
        `ProbeError` varchar(500) CHARACTER SET utf8mb4 NULL,
        `LastProbedAt` datetime(6) NULL,
        `CreatedAt` datetime(6) NOT NULL,
        `UpdatedAt` datetime(6) NOT NULL,
        CONSTRAINT `PK_ansheng_device_profiles` PRIMARY KEY (`Id`)
    ) CHARACTER SET=utf8mb4;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260803122119_AddAnShengProfileAndDiscoveredColumns') THEN

    CREATE INDEX `IX_discovered_ansheng_devices_ProbeStatus` ON `discovered_ansheng_devices` (`ProbeStatus`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260803122119_AddAnShengProfileAndDiscoveredColumns') THEN

    CREATE INDEX `IX_ansheng_device_profiles_AppCode_Imei` ON `ansheng_device_profiles` (`AppCode`, `Imei`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260803122119_AddAnShengProfileAndDiscoveredColumns') THEN

    CREATE INDEX `IX_ansheng_device_profiles_DeviceId` ON `ansheng_device_profiles` (`DeviceId`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260803122119_AddAnShengProfileAndDiscoveredColumns') THEN

    CREATE UNIQUE INDEX `IX_ansheng_device_profiles_Imei` ON `ansheng_device_profiles` (`Imei`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260803122119_AddAnShengProfileAndDiscoveredColumns') THEN

    INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
    VALUES ('20260803122119_AddAnShengProfileAndDiscoveredColumns', '8.0.11');

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

COMMIT;

START TRANSACTION;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260804023839_AddAnShengDeviceEvent') THEN

    CREATE TABLE `AnShengDeviceEvents` (
        `Id` bigint NOT NULL AUTO_INCREMENT,
        `AppCode` varchar(50) CHARACTER SET utf8mb4 NOT NULL,
        `Imei` varchar(32) CHARACTER SET utf8mb4 NOT NULL,
        `DeviceId` bigint NULL,
        `Method` varchar(32) CHARACTER SET utf8mb4 NOT NULL,
        `Kind` int NOT NULL,
        `Severity` int NOT NULL,
        `SlotNum` int NULL,
        `FrameId` varchar(64) CHARACTER SET utf8mb4 NULL,
        `OccurredAt` datetime(6) NOT NULL,
        `DeviceTimestampUtc` datetime(6) NULL,
        `ReceivedAt` datetime(6) NOT NULL,
        `PayloadJson` longtext CHARACTER SET utf8mb4 NULL,
        `RawJson` longtext CHARACTER SET utf8mb4 NULL,
        `DispatchedToRules` tinyint(1) NOT NULL,
        `DispatchError` varchar(512) CHARACTER SET utf8mb4 NULL,
        `CreatedAt` datetime(6) NOT NULL,
        CONSTRAINT `PK_AnShengDeviceEvents` PRIMARY KEY (`Id`)
    ) CHARACTER SET=utf8mb4;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260804023839_AddAnShengDeviceEvent') THEN

    CREATE INDEX `IX_AnShengDeviceEvents_AppCode_Kind_OccurredAt` ON `AnShengDeviceEvents` (`AppCode`, `Kind`, `OccurredAt`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260804023839_AddAnShengDeviceEvent') THEN

    CREATE INDEX `IX_AnShengDeviceEvents_DeviceId_OccurredAt` ON `AnShengDeviceEvents` (`DeviceId`, `OccurredAt`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260804023839_AddAnShengDeviceEvent') THEN

    CREATE INDEX `IX_AnShengDeviceEvents_Imei_OccurredAt` ON `AnShengDeviceEvents` (`Imei`, `OccurredAt`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260804023839_AddAnShengDeviceEvent') THEN

    INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
    VALUES ('20260804023839_AddAnShengDeviceEvent', '8.0.11');

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

COMMIT;

START TRANSACTION;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260804051159_AddAnShengCommandRecord') THEN

    CREATE TABLE `AnShengCommandRecords` (
        `Id` bigint NOT NULL AUTO_INCREMENT,
        `AppCode` varchar(50) CHARACTER SET utf8mb4 NOT NULL,
        `CommandId` varchar(36) CHARACTER SET utf8mb4 NOT NULL,
        `DeviceId` bigint NULL,
        `Imei` varchar(32) CHARACTER SET utf8mb4 NOT NULL,
        `Method` varchar(32) CHARACTER SET utf8mb4 NOT NULL,
        `FrameId` varchar(64) CHARACTER SET utf8mb4 NULL,
        `Status` int NOT NULL,
        `RejectReason` int NULL,
        `RequestJson` longtext CHARACTER SET utf8mb4 NOT NULL,
        `ResponseJson` longtext CHARACTER SET utf8mb4 NULL,
        `ErrorCode` varchar(64) CHARACTER SET utf8mb4 NULL,
        `ErrorMessage` varchar(512) CHARACTER SET utf8mb4 NULL,
        `IssuedAt` datetime(6) NOT NULL,
        `SentAt` datetime(6) NULL,
        `CompletedAt` datetime(6) NULL,
        `TimeoutAt` datetime(6) NULL,
        `DurationMs` int NULL,
        `OperatorUserId` bigint NULL,
        CONSTRAINT `PK_AnShengCommandRecords` PRIMARY KEY (`Id`)
    ) CHARACTER SET=utf8mb4;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260804051159_AddAnShengCommandRecord') THEN

    CREATE INDEX `IX_AnShengCommandRecords_AppCode_IssuedAt` ON `AnShengCommandRecords` (`AppCode`, `IssuedAt`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260804051159_AddAnShengCommandRecord') THEN

    CREATE UNIQUE INDEX `IX_AnShengCommandRecords_CommandId` ON `AnShengCommandRecords` (`CommandId`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260804051159_AddAnShengCommandRecord') THEN

    CREATE INDEX `IX_AnShengCommandRecords_DeviceId_IssuedAt` ON `AnShengCommandRecords` (`DeviceId`, `IssuedAt`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260804051159_AddAnShengCommandRecord') THEN

    CREATE INDEX `IX_AnShengCommandRecords_Imei_FrameId` ON `AnShengCommandRecords` (`Imei`, `FrameId`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260804051159_AddAnShengCommandRecord') THEN

    CREATE INDEX `IX_AnShengCommandRecords_Status_TimeoutAt` ON `AnShengCommandRecords` (`Status`, `TimeoutAt`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260804051159_AddAnShengCommandRecord') THEN

    INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
    VALUES ('20260804051159_AddAnShengCommandRecord', '8.0.11');

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

COMMIT;

START TRANSACTION;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260804112806_T8DelayTask') THEN

    ALTER TABLE `ansheng_device_profiles` ADD `SlotsSnapshot` longtext CHARACTER SET utf8mb4 NULL;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260804112806_T8DelayTask') THEN

    ALTER TABLE `ansheng_device_profiles` ADD `SlotsSnapshotAt` datetime(6) NULL;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260804112806_T8DelayTask') THEN

    CREATE TABLE `ansheng_delay_tasks` (
        `Id` bigint NOT NULL AUTO_INCREMENT,
        `AppCode` varchar(50) CHARACTER SET utf8mb4 NOT NULL,
        `DeviceId` bigint NOT NULL,
        `SlotNum` int NOT NULL,
        `Enable` tinyint(1) NOT NULL,
        `SAction` varchar(16) CHARACTER SET utf8mb4 NOT NULL,
        `EAction` varchar(16) CHARACTER SET utf8mb4 NOT NULL,
        `Secs` int NOT NULL,
        `Cnt` int NOT NULL,
        `SyncedAt` datetime(6) NOT NULL,
        CONSTRAINT `PK_ansheng_delay_tasks` PRIMARY KEY (`Id`)
    ) CHARACTER SET=utf8mb4;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260804112806_T8DelayTask') THEN

    CREATE INDEX `IX_ansheng_delay_tasks_AppCode_DeviceId` ON `ansheng_delay_tasks` (`AppCode`, `DeviceId`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260804112806_T8DelayTask') THEN

    CREATE INDEX `IX_ansheng_delay_tasks_DeviceId` ON `ansheng_delay_tasks` (`DeviceId`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260804112806_T8DelayTask') THEN

    CREATE UNIQUE INDEX `IX_ansheng_delay_tasks_DeviceId_SlotNum` ON `ansheng_delay_tasks` (`DeviceId`, `SlotNum`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260804112806_T8DelayTask') THEN

    INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
    VALUES ('20260804112806_T8DelayTask', '8.0.11');

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

COMMIT;

START TRANSACTION;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260804161127_T10TimeTask') THEN

    CREATE TABLE `ansheng_time_tasks` (
        `Id` bigint NOT NULL AUTO_INCREMENT,
        `AppCode` varchar(50) CHARACTER SET utf8mb4 NOT NULL,
        `DeviceId` bigint NOT NULL,
        `SlotNum` int NOT NULL,
        `TaskKind` int NOT NULL,
        `TaskIndex` int NOT NULL,
        `TaskId` varchar(32) CHARACTER SET utf8mb4 NOT NULL,
        `Enable` tinyint(1) NOT NULL,
        `WeekDays` varchar(64) CHARACTER SET utf8mb4 NOT NULL,
        `Hour` int NOT NULL,
        `Minute` int NOT NULL,
        `Action` varchar(16) CHARACTER SET utf8mb4 NOT NULL,
        `UploadEnable` tinyint(1) NOT NULL,
        `SHour` int NOT NULL,
        `SMinute` int NOT NULL,
        `EHour` int NOT NULL,
        `EMinute` int NOT NULL,
        `OnMins` int NOT NULL,
        `OffMins` int NOT NULL,
        `SyncedAt` datetime(6) NOT NULL,
        `RowVersion` bigint NOT NULL,
        CONSTRAINT `PK_ansheng_time_tasks` PRIMARY KEY (`Id`)
    ) CHARACTER SET=utf8mb4;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260804161127_T10TimeTask') THEN

    CREATE INDEX `IX_ansheng_time_tasks_AppCode_DeviceId` ON `ansheng_time_tasks` (`AppCode`, `DeviceId`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260804161127_T10TimeTask') THEN

    CREATE INDEX `IX_ansheng_time_tasks_DeviceId` ON `ansheng_time_tasks` (`DeviceId`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260804161127_T10TimeTask') THEN

    CREATE UNIQUE INDEX `IX_ansheng_time_tasks_DeviceId_SlotNum_TaskKind_TaskIndex` ON `ansheng_time_tasks` (`DeviceId`, `SlotNum`, `TaskKind`, `TaskIndex`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260804161127_T10TimeTask') THEN

    INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
    VALUES ('20260804161127_T10TimeTask', '8.0.11');

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

COMMIT;

START TRANSACTION;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260804181549_T11EmStatistic') THEN

    CREATE TABLE `ansheng_em_statistics` (
        `Id` bigint NOT NULL AUTO_INCREMENT,
        `AppCode` varchar(50) CHARACTER SET utf8mb4 NOT NULL,
        `DeviceId` bigint NOT NULL,
        `SlotNum` int NOT NULL,
        `Granularity` int NOT NULL,
        `PeriodKey` varchar(16) CHARACTER SET utf8mb4 NOT NULL,
        `Kwh` double NOT NULL,
        `SyncedAt` datetime(6) NOT NULL,
        CONSTRAINT `PK_ansheng_em_statistics` PRIMARY KEY (`Id`)
    ) CHARACTER SET=utf8mb4;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260804181549_T11EmStatistic') THEN

    CREATE INDEX `IX_ansheng_em_statistics_AppCode_DeviceId` ON `ansheng_em_statistics` (`AppCode`, `DeviceId`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260804181549_T11EmStatistic') THEN

    CREATE INDEX `IX_ansheng_em_statistics_DeviceId` ON `ansheng_em_statistics` (`DeviceId`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260804181549_T11EmStatistic') THEN

    CREATE UNIQUE INDEX `IX_ansheng_em_statistics_DeviceId_SlotNum_Granularity_PeriodKey` ON `ansheng_em_statistics` (`DeviceId`, `SlotNum`, `Granularity`, `PeriodKey`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260804181549_T11EmStatistic') THEN

    INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
    VALUES ('20260804181549_T11EmStatistic', '8.0.11');

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

COMMIT;

START TRANSACTION;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260806033128_AddDiscoveredAnShengUniqueImeiAppCode') THEN


    DELETE d
    FROM discovered_ansheng_devices d
    INNER JOIN (
        SELECT t.Imei,
               t.AppCode,
               CAST(SUBSTRING_INDEX(
                        GROUP_CONCAT(t.Id ORDER BY t.IsClaimed DESC, t.LastSeenAt DESC, t.Id DESC),
                        ',', 1) AS UNSIGNED) AS KeepId
        FROM discovered_ansheng_devices t
        GROUP BY t.Imei, t.AppCode
        HAVING COUNT(*) > 1
    ) k ON d.Imei = k.Imei AND d.AppCode <=> k.AppCode
    WHERE d.Id <> k.KeepId;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260806033128_AddDiscoveredAnShengUniqueImeiAppCode') THEN

    CREATE UNIQUE INDEX `IX_discovered_ansheng_devices_Imei_AppCode` ON `discovered_ansheng_devices` (`Imei`, `AppCode`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260806033128_AddDiscoveredAnShengUniqueImeiAppCode') THEN

    INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
    VALUES ('20260806033128_AddDiscoveredAnShengUniqueImeiAppCode', '8.0.11');

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

COMMIT;

START TRANSACTION;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20261105101315_AddDeviceAuthorizations') THEN

    CREATE TABLE `device_authorizations` (
        `Id` bigint NOT NULL AUTO_INCREMENT,
        `DeviceId` bigint NOT NULL,
        `AppCode` varchar(50) CHARACTER SET utf8mb4 NULL,
        `Status` varchar(20) CHARACTER SET utf8mb4 NOT NULL,
        `AuthorizedAt` datetime(6) NOT NULL,
        `ExpireAt` datetime(6) NOT NULL,
        `AuthorizedBy` bigint NULL,
        `AuthorizedByName` varchar(100) CHARACTER SET utf8mb4 NULL,
        `Remark` varchar(500) CHARACTER SET utf8mb4 NULL,
        `CreatedAt` datetime(6) NOT NULL,
        `UpdatedAt` datetime(6) NOT NULL,
        CONSTRAINT `PK_device_authorizations` PRIMARY KEY (`Id`),
        CONSTRAINT `FK_device_authorizations_devices_DeviceId` FOREIGN KEY (`DeviceId`) REFERENCES `devices` (`Id`) ON DELETE CASCADE
    ) CHARACTER SET=utf8mb4;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20261105101315_AddDeviceAuthorizations') THEN

    CREATE UNIQUE INDEX `IX_device_authorizations_DeviceId_AppCode` ON `device_authorizations` (`DeviceId`, `AppCode`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20261105101315_AddDeviceAuthorizations') THEN

    CREATE INDEX `IX_device_authorizations_ExpireAt` ON `device_authorizations` (`ExpireAt`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20261105101315_AddDeviceAuthorizations') THEN

    CREATE INDEX `IX_device_authorizations_Status` ON `device_authorizations` (`Status`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20261105101315_AddDeviceAuthorizations') THEN

    INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
    VALUES ('20261105101315_AddDeviceAuthorizations', '8.0.11');

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

COMMIT;

START TRANSACTION;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20261106064106_DictionaryItemTenantUnique') THEN

    CREATE UNIQUE INDEX `IX_dictionary_items_Type_Code_AppCode` ON `dictionary_items` (`Type`, `Code`, `AppCode`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20261106064106_DictionaryItemTenantUnique') THEN

    INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
    VALUES ('20261106064106_DictionaryItemTenantUnique', '8.0.11');

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

COMMIT;

START TRANSACTION;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20261107023459_BackfillLoginLogAppCode') THEN

    UPDATE login_logs SET AppCode = (SELECT u.AppCode FROM users u WHERE u.Id = login_logs.UserId) WHERE AppCode IS NULL;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20261107023459_BackfillLoginLogAppCode') THEN

    INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
    VALUES ('20261107023459_BackfillLoginLogAppCode', '8.0.11');

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

COMMIT;

-- ============================================================================
-- 多客户授权模型 T01：admin ↔ 客户授权中间表 user_customers
-- 迁移 ID 与 Migrations/20261123025525_AddUserCustomers.cs 保持一致。
-- 沿用本文件既有风格：用一次性存储过程 + __EFMigrationsHistory 保证幂等。
-- ============================================================================
START TRANSACTION;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20261123025525_AddUserCustomers') THEN

    CREATE TABLE IF NOT EXISTS `user_customers` (
        `Id` bigint NOT NULL AUTO_INCREMENT,
        `UserId` bigint NOT NULL,
        `CustomerId` bigint NOT NULL,
        `GrantedByUserId` bigint NULL,
        `Source` varchar(20) CHARACTER SET utf8mb4 NOT NULL DEFAULT 'manual',
        `CreatedAt` datetime(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
        `UpdatedAt` datetime(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3) ON UPDATE CURRENT_TIMESTAMP(3),
        `RevokedAt` datetime(3) NULL,
        CONSTRAINT `PK_user_customers` PRIMARY KEY (`Id`),
        CONSTRAINT `uk_user_customer` UNIQUE (`UserId`, `CustomerId`),
        KEY `idx_customer_id` (`CustomerId`),
        KEY `idx_granted_by` (`GrantedByUserId`),
        CONSTRAINT `FK_user_customers_customers_CustomerId` FOREIGN KEY (`CustomerId`) REFERENCES `customers` (`Id`) ON DELETE RESTRICT,
        CONSTRAINT `FK_user_customers_users_GrantedByUserId` FOREIGN KEY (`GrantedByUserId`) REFERENCES `users` (`Id`) ON DELETE SET NULL,
        CONSTRAINT `FK_user_customers_users_UserId` FOREIGN KEY (`UserId`) REFERENCES `users` (`Id`) ON DELETE CASCADE
    ) CHARACTER SET=utf8mb4;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20261123025525_AddUserCustomers') THEN

    -- 存量回填：CustomerId 优先、AppCode 兜底；仅 role='admin'。
    -- NOT EXISTS 守卫保证重复执行不撞 uk_user_customer。
    INSERT INTO user_customers
        (UserId, CustomerId, GrantedByUserId, Source, CreatedAt, UpdatedAt)
    SELECT u.Id, c.Id, NULL, 'migration', NOW(3), NOW(3)
    FROM users u
    JOIN customers c
      ON (u.CustomerId IS NOT NULL AND u.CustomerId = c.Id)
      OR (u.CustomerId IS NULL AND u.AppCode IS NOT NULL AND u.AppCode = c.AppCode)
    WHERE u.Role = 'admin'
      AND (u.CustomerId IS NOT NULL OR u.AppCode IS NOT NULL)
      AND NOT EXISTS (
          SELECT 1 FROM user_customers uc
          WHERE uc.UserId = u.Id AND uc.CustomerId = c.Id);

    -- 清空 admin 单值列；super_admin 与下级账号不动。
    UPDATE users SET CustomerId = NULL, AppCode = NULL, UpdatedAt = NOW(3) WHERE Role = 'admin';

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20261123025525_AddUserCustomers') THEN

    INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
    VALUES ('20261123025525_AddUserCustomers', '8.0.11');

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

COMMIT;

-- ----------------------------------------------------------------------------
-- 幂等 seed 段：本脚本只建库结构，业务种子由应用启动时的 SeedUsers 写入。
-- 若脚本执行时库内已有 admin 用户，则按原单值回填；
-- 另按种子固定映射 manager(Id=2) → customer_001(Id=1) 兜底，
-- 保证「全新空库按脚本建库 + 应用种子」后 manager 的授权集合恰为 [1]。
-- 两段均带 NOT EXISTS 守卫，可重复执行。
-- ----------------------------------------------------------------------------
START TRANSACTION;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    INSERT INTO user_customers
        (UserId, CustomerId, GrantedByUserId, Source, CreatedAt, UpdatedAt)
    SELECT u.Id, c.Id, NULL, 'seed', NOW(3), NOW(3)
    FROM users u
    JOIN customers c
      ON (u.CustomerId IS NOT NULL AND u.CustomerId = c.Id)
      OR (u.CustomerId IS NULL AND u.AppCode IS NOT NULL AND u.AppCode = c.AppCode)
    WHERE u.Role = 'admin'
      AND (u.CustomerId IS NOT NULL OR u.AppCode IS NOT NULL)
      AND NOT EXISTS (
          SELECT 1 FROM user_customers uc
          WHERE uc.UserId = u.Id AND uc.CustomerId = c.Id);

    INSERT INTO user_customers
        (UserId, CustomerId, GrantedByUserId, Source, CreatedAt, UpdatedAt)
    SELECT 2, 1, NULL, 'seed', NOW(3), NOW(3)
    FROM DUAL
    WHERE EXISTS (SELECT 1 FROM users u WHERE u.Id = 2 AND u.Role = 'admin')
      AND EXISTS (SELECT 1 FROM customers c WHERE c.Id = 1)
      AND NOT EXISTS (SELECT 1 FROM user_customers uc WHERE uc.UserId = 2 AND uc.CustomerId = 1);
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

COMMIT;

