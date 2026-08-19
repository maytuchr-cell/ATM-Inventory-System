CREATE TABLE IF NOT EXISTS `__EFMigrationsHistory` (
    `MigrationId` varchar(150) NOT NULL,
    `ProductVersion` varchar(32) NOT NULL,
    PRIMARY KEY (`MigrationId`)
);

START TRANSACTION;
CREATE TABLE `AtmModels` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `ModelCode` longtext NOT NULL,
    `ModelName` longtext NOT NULL,
    `Manufacturer` longtext NULL,
    `Description` longtext NULL,
    `IsActive` tinyint(1) NOT NULL,
    PRIMARY KEY (`Id`)
);

CREATE TABLE `AuditLogs` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `EntityType` varchar(255) NOT NULL,
    `EntityId` varchar(255) NOT NULL,
    `Action` longtext NOT NULL,
    `OldValues` longtext NULL,
    `NewValues` longtext NULL,
    `UserId` longtext NOT NULL,
    `UserName` longtext NOT NULL,
    `Timestamp` datetime(6) NOT NULL,
    PRIMARY KEY (`Id`)
);

CREATE TABLE `Categories` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `Name` longtext NOT NULL,
    `Description` longtext NULL,
    `IsActive` tinyint(1) NOT NULL,
    PRIMARY KEY (`Id`)
);

CREATE TABLE `DisposalRequests` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `PartNo` longtext NOT NULL,
    `SerialNo` longtext NULL,
    `LocationId` int NOT NULL,
    `Qty` int NOT NULL,
    `Status` longtext NOT NULL,
    `ReasonCode` longtext NOT NULL,
    `RequestedBy` longtext NOT NULL,
    `ApprovedBy` longtext NULL,
    `CreatedAt` datetime(6) NOT NULL,
    `ApprovedAt` datetime(6) NULL,
    `DisposedAt` datetime(6) NULL,
    PRIMARY KEY (`Id`)
);

CREATE TABLE `EquivalentGroups` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `Name` longtext NOT NULL,
    `Description` longtext NULL,
    `CreatedAt` datetime(6) NOT NULL,
    PRIMARY KEY (`Id`)
);

CREATE TABLE `EquivalentParts` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `OriginalPartNo` varchar(255) NOT NULL,
    `EquivalentPartNo` varchar(255) NOT NULL,
    PRIMARY KEY (`Id`)
);

CREATE TABLE `Locations` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `Name` longtext NOT NULL,
    `Code` longtext NOT NULL,
    `LocationType` longtext NOT NULL,
    `IsActive` tinyint(1) NOT NULL,
    PRIMARY KEY (`Id`)
);

CREATE TABLE `StockCounts` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `CountType` longtext NOT NULL,
    `Period` longtext NOT NULL,
    `Status` longtext NOT NULL,
    `IsSystemFrozen` tinyint(1) NOT NULL,
    `StartedBy` longtext NOT NULL,
    `CreatedAt` datetime(6) NOT NULL,
    `CompletedAt` datetime(6) NULL,
    PRIMARY KEY (`Id`)
);

CREATE TABLE `StockMovements` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `MovementType` longtext NOT NULL,
    `PartNo` varchar(255) NOT NULL,
    `FromLocationId` int NULL,
    `ToLocationId` int NULL,
    `Qty` int NOT NULL,
    `Condition` longtext NOT NULL,
    `RefType` longtext NULL,
    `RefId` longtext NULL,
    `Cost` decimal(18,2) NULL,
    `SerialNo` longtext NULL,
    `Remarks` longtext NULL,
    `UserName` longtext NOT NULL,
    `Timestamp` datetime(6) NOT NULL,
    PRIMARY KEY (`Id`)
);

CREATE TABLE `StockTransfers` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `PartNo` longtext NOT NULL,
    `Qty` int NOT NULL,
    `Condition` longtext NOT NULL,
    `FromLocationId` int NOT NULL,
    `ToLocationId` int NOT NULL,
    `Status` longtext NOT NULL,
    `RequestedBy` longtext NOT NULL,
    `ApprovedBy` longtext NULL,
    `CreatedAt` datetime(6) NOT NULL,
    `ApprovedAt` datetime(6) NULL,
    `ConfirmedAt` datetime(6) NULL,
    `ReceivedAt` datetime(6) NULL,
    PRIMARY KEY (`Id`)
);

CREATE TABLE `SystemSettings` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `IsFrozen` tinyint(1) NOT NULL,
    `ActiveStockCountId` int NULL,
    PRIMARY KEY (`Id`)
);

CREATE TABLE `Tickets` (
    `TicketId` int NOT NULL AUTO_INCREMENT,
    `TechEmail` longtext NOT NULL,
    `TechId` longtext NOT NULL,
    `TechName` longtext NOT NULL,
    `TechPhone` longtext NOT NULL,
    `TechDept` longtext NOT NULL,
    `RequestedPartNo` longtext NULL,
    `ApprovedPartNo` longtext NULL,
    `FaultySerialNo` longtext NULL,
    `FaultyPartNo` longtext NULL,
    `MachineModel` longtext NULL,
    `Description` longtext NULL,
    `AttachmentPath` longtext NULL,
    `MainCause` longtext NULL,
    `LogisticsCost` decimal(18,2) NULL,
    `Status` longtext NOT NULL,
    `IsDOA` tinyint(1) NOT NULL,
    `CreatedAt` datetime(6) NOT NULL,
    `ReceivedAt` datetime(6) NULL,
    `DueDate` datetime(6) NULL,
    PRIMARY KEY (`TicketId`)
);

CREATE TABLE `Users` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `Email` varchar(255) NOT NULL,
    `PasswordHash` longtext NOT NULL,
    `Role` longtext NOT NULL,
    `Name` longtext NOT NULL,
    `IsActive` tinyint(1) NOT NULL,
    `CreatedAt` datetime(6) NOT NULL,
    PRIMARY KEY (`Id`)
);

CREATE TABLE `Vendors` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `Name` longtext NOT NULL,
    `Code` longtext NOT NULL,
    `VendorType` longtext NOT NULL,
    `ContactInfo` longtext NULL,
    `IsActive` tinyint(1) NOT NULL,
    PRIMARY KEY (`Id`)
);

CREATE TABLE `AtmModelParts` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `AtmModelId` int NOT NULL,
    `PartNo` varchar(255) NOT NULL,
    PRIMARY KEY (`Id`),
    CONSTRAINT `FK_AtmModelParts_AtmModels_AtmModelId` FOREIGN KEY (`AtmModelId`) REFERENCES `AtmModels` (`Id`) ON DELETE CASCADE
);

CREATE TABLE `Parts` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `OrderNumber` longtext NOT NULL,
    `PartNo` varchar(255) NOT NULL,
    `PartName` longtext NOT NULL,
    `Unit` longtext NOT NULL,
    `SerialNo` longtext NULL,
    `StockQuantity` int NOT NULL,
    `CategoryId` int NULL,
    `CatalogueRef` longtext NULL,
    `MinStock` int NOT NULL,
    `MaxStock` int NOT NULL,
    `ReorderPoint` int NOT NULL,
    `TrackingNumber` longtext NULL,
    `Aging` int NULL,
    `CostPerUnit` decimal(18,2) NULL,
    `IsActive` tinyint(1) NOT NULL,
    `CreatedAt` datetime(6) NOT NULL,
    `UpdatedAt` datetime(6) NOT NULL,
    `ExpiryDate` datetime(6) NULL,
    `IsUnrepairable` tinyint(1) NOT NULL,
    `MainUnit` longtext NULL,
    `Remark` longtext NULL,
    `ImagePath` longtext NULL,
    PRIMARY KEY (`Id`),
    CONSTRAINT `FK_Parts_Categories_CategoryId` FOREIGN KEY (`CategoryId`) REFERENCES `Categories` (`Id`) ON DELETE SET NULL
);

CREATE TABLE `EquivalentGroupMembers` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `GroupId` int NOT NULL,
    `PartNo` varchar(255) NOT NULL,
    PRIMARY KEY (`Id`),
    CONSTRAINT `FK_EquivalentGroupMembers_EquivalentGroups_GroupId` FOREIGN KEY (`GroupId`) REFERENCES `EquivalentGroups` (`Id`) ON DELETE CASCADE
);

CREATE TABLE `StockCountLines` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `StockCountId` int NOT NULL,
    `PartNo` longtext NOT NULL,
    `LocationId` int NOT NULL,
    `SystemQty` int NOT NULL,
    `PhysicalQty` int NULL,
    `AdjustApproved` tinyint(1) NOT NULL,
    `Remarks` longtext NULL,
    PRIMARY KEY (`Id`),
    CONSTRAINT `FK_StockCountLines_StockCounts_StockCountId` FOREIGN KEY (`StockCountId`) REFERENCES `StockCounts` (`Id`) ON DELETE CASCADE
);

CREATE TABLE `ReturnRequests` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `TicketId` int NOT NULL,
    `PartNo` longtext NOT NULL,
    `Condition` longtext NOT NULL,
    `SourceType` longtext NOT NULL,
    `LocationFromId` int NOT NULL,
    `LocationToId` int NOT NULL,
    `ReturnedBy` longtext NOT NULL,
    `CreatedAt` datetime(6) NOT NULL,
    PRIMARY KEY (`Id`),
    CONSTRAINT `FK_ReturnRequests_Tickets_TicketId` FOREIGN KEY (`TicketId`) REFERENCES `Tickets` (`TicketId`) ON DELETE RESTRICT
);

CREATE TABLE `GoodsReceipts` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `ReceiptNo` longtext NOT NULL,
    `Source` longtext NOT NULL,
    `VendorId` int NULL,
    `RefDocument` longtext NULL,
    `LocationId` int NOT NULL,
    `ReceivedBy` longtext NOT NULL,
    `ReceivedAt` datetime(6) NOT NULL,
    `HandlingCost` decimal(18,2) NULL,
    PRIMARY KEY (`Id`),
    CONSTRAINT `FK_GoodsReceipts_Locations_LocationId` FOREIGN KEY (`LocationId`) REFERENCES `Locations` (`Id`) ON DELETE CASCADE,
    CONSTRAINT `FK_GoodsReceipts_Vendors_VendorId` FOREIGN KEY (`VendorId`) REFERENCES `Vendors` (`Id`)
);

CREATE TABLE `PartStocks` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `PartId` int NOT NULL,
    `LocationId` int NOT NULL,
    `GoodQty` int NOT NULL,
    `DefectiveQty` int NOT NULL,
    PRIMARY KEY (`Id`),
    CONSTRAINT `FK_PartStocks_Locations_LocationId` FOREIGN KEY (`LocationId`) REFERENCES `Locations` (`Id`) ON DELETE RESTRICT,
    CONSTRAINT `FK_PartStocks_Parts_PartId` FOREIGN KEY (`PartId`) REFERENCES `Parts` (`Id`) ON DELETE CASCADE
);

CREATE TABLE `GoodsReceiptLines` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `GoodsReceiptId` int NOT NULL,
    `PartNo` longtext NOT NULL,
    `Qty` int NOT NULL,
    `Condition` longtext NOT NULL,
    `SerialNo` longtext NULL,
    `IsManualAdjust` tinyint(1) NOT NULL,
    `Remarks` longtext NULL,
    PRIMARY KEY (`Id`),
    CONSTRAINT `FK_GoodsReceiptLines_GoodsReceipts_GoodsReceiptId` FOREIGN KEY (`GoodsReceiptId`) REFERENCES `GoodsReceipts` (`Id`) ON DELETE CASCADE
);

CREATE UNIQUE INDEX `IX_AtmModelParts_AtmModelId_PartNo` ON `AtmModelParts` (`AtmModelId`, `PartNo`);

CREATE INDEX `IX_AuditLogs_EntityType_EntityId` ON `AuditLogs` (`EntityType`, `EntityId`);

CREATE UNIQUE INDEX `IX_EquivalentGroupMembers_GroupId_PartNo` ON `EquivalentGroupMembers` (`GroupId`, `PartNo`);

CREATE UNIQUE INDEX `IX_EquivalentParts_OriginalPartNo_EquivalentPartNo` ON `EquivalentParts` (`OriginalPartNo`, `EquivalentPartNo`);

CREATE INDEX `IX_GoodsReceiptLines_GoodsReceiptId` ON `GoodsReceiptLines` (`GoodsReceiptId`);

CREATE INDEX `IX_GoodsReceipts_LocationId` ON `GoodsReceipts` (`LocationId`);

CREATE INDEX `IX_GoodsReceipts_VendorId` ON `GoodsReceipts` (`VendorId`);

CREATE INDEX `IX_Parts_CategoryId` ON `Parts` (`CategoryId`);

CREATE UNIQUE INDEX `IX_Parts_PartNo` ON `Parts` (`PartNo`);

CREATE INDEX `IX_PartStocks_LocationId` ON `PartStocks` (`LocationId`);

CREATE UNIQUE INDEX `IX_PartStocks_PartId_LocationId` ON `PartStocks` (`PartId`, `LocationId`);

CREATE INDEX `IX_ReturnRequests_TicketId` ON `ReturnRequests` (`TicketId`);

CREATE INDEX `IX_StockCountLines_StockCountId` ON `StockCountLines` (`StockCountId`);

CREATE INDEX `IX_StockMovements_PartNo_Timestamp` ON `StockMovements` (`PartNo`, `Timestamp`);

CREATE UNIQUE INDEX `IX_Users_Email` ON `Users` (`Email`);

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260818171426_InitialCreate', '9.0.19');

COMMIT;

