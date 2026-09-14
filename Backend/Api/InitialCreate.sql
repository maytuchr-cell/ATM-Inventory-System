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

CREATE TABLE `DailyReportImportBatches` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `FileName` longtext NOT NULL,
    `ImportedAt` datetime(6) NOT NULL,
    `ImportedBy` longtext NOT NULL,
    `TotalRows` int NOT NULL,
    `ReturnConfirmedCount` int NOT NULL,
    `RepairCompletedCount` int NOT NULL,
    `StillInRepairCount` int NOT NULL,
    `UnmatchedCount` int NOT NULL,
    `OutboundCount` int NOT NULL,
    `InboundRepairedCount` int NOT NULL,
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

CREATE TABLE `FeContacts` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `FeId` longtext NOT NULL,
    `FeName` longtext NOT NULL,
    `Tel` longtext NULL,
    `Address` longtext NOT NULL,
    `Postcode` longtext NULL,
    `UpdatedAt` datetime(6) NOT NULL,
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

CREATE TABLE `SavedAddresses` (
    `SavedAddressId` int NOT NULL AUTO_INCREMENT,
    `TechEmail` longtext NOT NULL,
    `Label` longtext NOT NULL,
    `Address` longtext NOT NULL,
    `CreatedAt` datetime(6) NOT NULL,
    PRIMARY KEY (`SavedAddressId`)
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

CREATE TABLE `SystemSettings` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `IsFrozen` tinyint(1) NOT NULL,
    `ActiveStockCountId` int NULL,
    PRIMARY KEY (`Id`)
);

CREATE TABLE `Tickets` (
    `TicketId` int NOT NULL AUTO_INCREMENT,
    `ExternalTicketNo` varchar(255) NOT NULL,
    `TechEmail` longtext NOT NULL,
    `TechName` longtext NOT NULL,
    `TechDept` longtext NOT NULL,
    `Status` longtext NULL,
    `RejectReason` longtext NULL,
    `ApproverName` longtext NULL,
    `ApprovedAt` datetime(6) NULL,
    `ReturnAddress` longtext NULL,
    `ReturnEmailSentAt` datetime(6) NULL,
    `CreatedAt` datetime(6) NOT NULL,
    `UpdatedAt` datetime(6) NOT NULL,
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

CREATE TABLE `Parts` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `OrderNumber` longtext NOT NULL,
    `PartNo` varchar(255) NOT NULL,
    `PartName` longtext NOT NULL,
    `Unit` longtext NOT NULL,
    `SerialNo` longtext NULL,
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
    `Zone` longtext NULL,
    `DeviceType` longtext NULL,
    `AddedBy` longtext NULL,
    `Lot` longtext NULL,
    `Project` longtext NULL,
    `AddedDate` datetime(6) NULL,
    `RowVersion` longtext NOT NULL,
    PRIMARY KEY (`Id`),
    CONSTRAINT `FK_Parts_Categories_CategoryId` FOREIGN KEY (`CategoryId`) REFERENCES `Categories` (`Id`) ON DELETE SET NULL
);

CREATE TABLE `DailyReportImportRows` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `BatchId` int NOT NULL,
    `SourceSheet` longtext NOT NULL,
    `RowIndex` int NOT NULL,
    `PartNo` longtext NOT NULL,
    `PartName` longtext NOT NULL,
    `SerialNo` longtext NOT NULL,
    `Qty` int NOT NULL,
    `DhlStatus` longtext NOT NULL,
    `Problem` longtext NULL,
    `FeName` longtext NULL,
    `CaseNo` longtext NULL,
    `MatchType` longtext NOT NULL,
    `TicketId` int NULL,
    `WithdrawBatchId` int NULL,
    `PartUnitId` int NULL,
    `StockCredited` tinyint(1) NOT NULL,
    `Undone` tinyint(1) NOT NULL,
    `UndoneAt` datetime(6) NULL,
    PRIMARY KEY (`Id`),
    CONSTRAINT `FK_DailyReportImportRows_DailyReportImportBatches_BatchId` FOREIGN KEY (`BatchId`) REFERENCES `DailyReportImportBatches` (`Id`) ON DELETE CASCADE
);

CREATE TABLE `WithdrawBatches` (
    `WithdrawBatchId` int NOT NULL AUTO_INCREMENT,
    `TicketId` int NOT NULL,
    `Status` longtext NULL,
    `RejectReason` longtext NULL,
    `ApproverName` longtext NULL,
    `ApprovedAt` datetime(6) NULL,
    `EmailSentAt` datetime(6) NULL,
    `WithdrawAddress` longtext NULL,
    `WithdrawDescription` longtext NULL,
    `WithdrawSlipNo` longtext NULL,
    `WithdrawDate` datetime(6) NULL,
    `EmployeeCode` longtext NULL,
    `UsageStatus` longtext NULL,
    `TechSupportName` longtext NULL,
    `NeededByDate` datetime(6) NULL,
    `FeId` longtext NULL,
    `Sla` longtext NULL,
    `AtmCode` longtext NULL,
    `WaitingSinceAt` datetime(6) NULL,
    `CreatedAt` datetime(6) NOT NULL,
    `UpdatedAt` datetime(6) NOT NULL,
    `ReturnStatus` longtext NULL,
    `ReturnRejectReason` longtext NULL,
    `ReturnApproverName` longtext NULL,
    `ReturnApprovedAt` datetime(6) NULL,
    `ReturnAddress` longtext NULL,
    `ReturnEmailSentAt` datetime(6) NULL,
    `ReturnSlipNo` longtext NULL,
    `ReturnRequestedAt` datetime(6) NULL,
    PRIMARY KEY (`WithdrawBatchId`),
    CONSTRAINT `FK_WithdrawBatches_Tickets_TicketId` FOREIGN KEY (`TicketId`) REFERENCES `Tickets` (`TicketId`) ON DELETE CASCADE
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

CREATE TABLE `AtmModelParts` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `AtmModelId` int NOT NULL,
    `PartId` int NOT NULL,
    `PartNo` varchar(255) NOT NULL,
    PRIMARY KEY (`Id`),
    CONSTRAINT `FK_AtmModelParts_AtmModels_AtmModelId` FOREIGN KEY (`AtmModelId`) REFERENCES `AtmModels` (`Id`) ON DELETE CASCADE,
    CONSTRAINT `FK_AtmModelParts_Parts_PartId` FOREIGN KEY (`PartId`) REFERENCES `Parts` (`Id`) ON DELETE RESTRICT
);

CREATE TABLE `DisposalRequests` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `PartId` int NOT NULL,
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
    PRIMARY KEY (`Id`),
    CONSTRAINT `FK_DisposalRequests_Parts_PartId` FOREIGN KEY (`PartId`) REFERENCES `Parts` (`Id`) ON DELETE RESTRICT
);

CREATE TABLE `EquivalentGroupMembers` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `GroupId` int NOT NULL,
    `PartId` int NOT NULL,
    `PartNo` varchar(255) NOT NULL,
    PRIMARY KEY (`Id`),
    CONSTRAINT `FK_EquivalentGroupMembers_EquivalentGroups_GroupId` FOREIGN KEY (`GroupId`) REFERENCES `EquivalentGroups` (`Id`) ON DELETE CASCADE,
    CONSTRAINT `FK_EquivalentGroupMembers_Parts_PartId` FOREIGN KEY (`PartId`) REFERENCES `Parts` (`Id`) ON DELETE RESTRICT
);

CREATE TABLE `PartImages` (
    `PartImageId` int NOT NULL AUTO_INCREMENT,
    `PartId` int NOT NULL,
    `FilePath` longtext NOT NULL,
    `FileName` longtext NOT NULL,
    `SortOrder` int NOT NULL,
    `UploadedAt` datetime(6) NOT NULL,
    PRIMARY KEY (`PartImageId`),
    CONSTRAINT `FK_PartImages_Parts_PartId` FOREIGN KEY (`PartId`) REFERENCES `Parts` (`Id`) ON DELETE CASCADE
);

CREATE TABLE `PartStocks` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `PartId` int NOT NULL,
    `LocationId` int NOT NULL,
    `GoodQty` int NOT NULL,
    `BadQty` int NOT NULL,
    `RepairQty` int NOT NULL,
    `UpdatedAt` datetime(6) NOT NULL,
    `RowVersion` longtext NOT NULL,
    PRIMARY KEY (`Id`),
    CONSTRAINT `FK_PartStocks_Locations_LocationId` FOREIGN KEY (`LocationId`) REFERENCES `Locations` (`Id`) ON DELETE RESTRICT,
    CONSTRAINT `FK_PartStocks_Parts_PartId` FOREIGN KEY (`PartId`) REFERENCES `Parts` (`Id`) ON DELETE CASCADE
);

CREATE TABLE `PartUnits` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `PartId` int NOT NULL,
    `LocationId` int NULL,
    `SerialNo` varchar(255) NOT NULL,
    `Condition` longtext NOT NULL,
    `ExpiryDate` datetime(6) NULL,
    `IsUnrepairable` tinyint(1) NOT NULL,
    `ReceivedAt` datetime(6) NOT NULL,
    `Status` longtext NOT NULL,
    PRIMARY KEY (`Id`),
    CONSTRAINT `FK_PartUnits_Locations_LocationId` FOREIGN KEY (`LocationId`) REFERENCES `Locations` (`Id`) ON DELETE SET NULL,
    CONSTRAINT `FK_PartUnits_Parts_PartId` FOREIGN KEY (`PartId`) REFERENCES `Parts` (`Id`) ON DELETE CASCADE
);

CREATE TABLE `ReturnRequests` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `TicketId` int NOT NULL,
    `PartId` int NOT NULL,
    `PartNo` longtext NOT NULL,
    `Condition` longtext NOT NULL,
    `SourceType` longtext NOT NULL,
    `LocationFromId` int NOT NULL,
    `LocationToId` int NOT NULL,
    `ReturnedBy` longtext NOT NULL,
    `CreatedAt` datetime(6) NOT NULL,
    PRIMARY KEY (`Id`),
    CONSTRAINT `FK_ReturnRequests_Parts_PartId` FOREIGN KEY (`PartId`) REFERENCES `Parts` (`Id`) ON DELETE RESTRICT,
    CONSTRAINT `FK_ReturnRequests_Tickets_TicketId` FOREIGN KEY (`TicketId`) REFERENCES `Tickets` (`TicketId`) ON DELETE RESTRICT
);

CREATE TABLE `StockCountLines` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `StockCountId` int NOT NULL,
    `PartId` int NOT NULL,
    `PartNo` longtext NOT NULL,
    `LocationId` int NOT NULL,
    `SystemQty` int NOT NULL,
    `PhysicalQty` int NULL,
    `AdjustApproved` tinyint(1) NOT NULL,
    `Remarks` longtext NULL,
    PRIMARY KEY (`Id`),
    CONSTRAINT `FK_StockCountLines_Parts_PartId` FOREIGN KEY (`PartId`) REFERENCES `Parts` (`Id`) ON DELETE RESTRICT,
    CONSTRAINT `FK_StockCountLines_StockCounts_StockCountId` FOREIGN KEY (`StockCountId`) REFERENCES `StockCounts` (`Id`) ON DELETE CASCADE
);

CREATE TABLE `StockTransfers` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `PartId` int NOT NULL,
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
    PRIMARY KEY (`Id`),
    CONSTRAINT `FK_StockTransfers_Parts_PartId` FOREIGN KEY (`PartId`) REFERENCES `Parts` (`Id`) ON DELETE RESTRICT
);

CREATE TABLE `TicketAttachments` (
    `TicketAttachmentId` int NOT NULL AUTO_INCREMENT,
    `TicketId` int NOT NULL,
    `WithdrawBatchId` int NULL,
    `Phase` longtext NOT NULL,
    `FilePath` longtext NOT NULL,
    `FileName` longtext NOT NULL,
    `UploadedAt` datetime(6) NOT NULL,
    PRIMARY KEY (`TicketAttachmentId`),
    CONSTRAINT `FK_TicketAttachments_Tickets_TicketId` FOREIGN KEY (`TicketId`) REFERENCES `Tickets` (`TicketId`) ON DELETE CASCADE,
    CONSTRAINT `FK_TicketAttachments_WithdrawBatches_WithdrawBatchId` FOREIGN KEY (`WithdrawBatchId`) REFERENCES `WithdrawBatches` (`WithdrawBatchId`) ON DELETE CASCADE
);

CREATE TABLE `TicketPartLines` (
    `TicketPartLineId` int NOT NULL AUTO_INCREMENT,
    `TicketId` int NOT NULL,
    `WithdrawBatchId` int NULL,
    `PartId` int NOT NULL,
    `PartNo` longtext NOT NULL,
    `OriginalPartNo` longtext NULL,
    `Quantity` int NOT NULL,
    `LineType` longtext NOT NULL,
    `ConfirmedQty` int NOT NULL,
    `Condition` longtext NULL,
    `Problem` longtext NULL,
    `SerialNo` longtext NULL,
    `CreatedAt` datetime(6) NOT NULL,
    PRIMARY KEY (`TicketPartLineId`),
    CONSTRAINT `FK_TicketPartLines_Parts_PartId` FOREIGN KEY (`PartId`) REFERENCES `Parts` (`Id`) ON DELETE RESTRICT,
    CONSTRAINT `FK_TicketPartLines_Tickets_TicketId` FOREIGN KEY (`TicketId`) REFERENCES `Tickets` (`TicketId`) ON DELETE CASCADE,
    CONSTRAINT `FK_TicketPartLines_WithdrawBatches_WithdrawBatchId` FOREIGN KEY (`WithdrawBatchId`) REFERENCES `WithdrawBatches` (`WithdrawBatchId`) ON DELETE CASCADE
);

CREATE TABLE `GoodsReceiptLines` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `GoodsReceiptId` int NOT NULL,
    `PartId` int NOT NULL,
    `PartNo` longtext NOT NULL,
    `Qty` int NOT NULL,
    `Condition` longtext NOT NULL,
    `SerialNo` longtext NULL,
    `IsManualAdjust` tinyint(1) NOT NULL,
    `Remarks` longtext NULL,
    PRIMARY KEY (`Id`),
    CONSTRAINT `FK_GoodsReceiptLines_GoodsReceipts_GoodsReceiptId` FOREIGN KEY (`GoodsReceiptId`) REFERENCES `GoodsReceipts` (`Id`) ON DELETE CASCADE,
    CONSTRAINT `FK_GoodsReceiptLines_Parts_PartId` FOREIGN KEY (`PartId`) REFERENCES `Parts` (`Id`) ON DELETE RESTRICT
);

CREATE TABLE `StockMovements` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `MovementType` longtext NOT NULL,
    `PartId` int NOT NULL,
    `PartNo` varchar(255) NOT NULL,
    `FromLocationId` int NULL,
    `ToLocationId` int NULL,
    `PartUnitId` int NULL,
    `Qty` int NOT NULL,
    `Condition` longtext NOT NULL,
    `RefType` longtext NULL,
    `RefId` longtext NULL,
    `Cost` decimal(18,2) NULL,
    `SerialNo` longtext NULL,
    `Remarks` longtext NULL,
    `UserName` longtext NOT NULL,
    `Timestamp` datetime(6) NOT NULL,
    PRIMARY KEY (`Id`),
    CONSTRAINT `FK_StockMovements_PartUnits_PartUnitId` FOREIGN KEY (`PartUnitId`) REFERENCES `PartUnits` (`Id`) ON DELETE SET NULL,
    CONSTRAINT `FK_StockMovements_Parts_PartId` FOREIGN KEY (`PartId`) REFERENCES `Parts` (`Id`) ON DELETE RESTRICT
);

CREATE UNIQUE INDEX `IX_AtmModelParts_AtmModelId_PartNo` ON `AtmModelParts` (`AtmModelId`, `PartNo`);

CREATE INDEX `IX_AtmModelParts_PartId` ON `AtmModelParts` (`PartId`);

CREATE INDEX `IX_AuditLogs_EntityType_EntityId` ON `AuditLogs` (`EntityType`, `EntityId`);

CREATE INDEX `IX_DailyReportImportRows_BatchId` ON `DailyReportImportRows` (`BatchId`);

CREATE INDEX `IX_DisposalRequests_PartId` ON `DisposalRequests` (`PartId`);

CREATE UNIQUE INDEX `IX_EquivalentGroupMembers_GroupId_PartNo` ON `EquivalentGroupMembers` (`GroupId`, `PartNo`);

CREATE INDEX `IX_EquivalentGroupMembers_PartId` ON `EquivalentGroupMembers` (`PartId`);

CREATE UNIQUE INDEX `IX_EquivalentParts_OriginalPartNo_EquivalentPartNo` ON `EquivalentParts` (`OriginalPartNo`, `EquivalentPartNo`);

CREATE INDEX `IX_GoodsReceiptLines_GoodsReceiptId` ON `GoodsReceiptLines` (`GoodsReceiptId`);

CREATE INDEX `IX_GoodsReceiptLines_PartId` ON `GoodsReceiptLines` (`PartId`);

CREATE INDEX `IX_GoodsReceipts_LocationId` ON `GoodsReceipts` (`LocationId`);

CREATE INDEX `IX_GoodsReceipts_VendorId` ON `GoodsReceipts` (`VendorId`);

CREATE INDEX `IX_PartImages_PartId` ON `PartImages` (`PartId`);

CREATE INDEX `IX_Parts_CategoryId` ON `Parts` (`CategoryId`);

CREATE UNIQUE INDEX `IX_Parts_PartNo` ON `Parts` (`PartNo`);

CREATE INDEX `IX_PartStocks_LocationId` ON `PartStocks` (`LocationId`);

CREATE UNIQUE INDEX `IX_PartStocks_PartId_LocationId` ON `PartStocks` (`PartId`, `LocationId`);

CREATE INDEX `IX_PartUnits_LocationId` ON `PartUnits` (`LocationId`);

CREATE INDEX `IX_PartUnits_PartId` ON `PartUnits` (`PartId`);

CREATE UNIQUE INDEX `IX_PartUnits_SerialNo` ON `PartUnits` (`SerialNo`);

CREATE INDEX `IX_ReturnRequests_PartId` ON `ReturnRequests` (`PartId`);

CREATE INDEX `IX_ReturnRequests_TicketId` ON `ReturnRequests` (`TicketId`);

CREATE INDEX `IX_StockCountLines_PartId` ON `StockCountLines` (`PartId`);

CREATE INDEX `IX_StockCountLines_StockCountId` ON `StockCountLines` (`StockCountId`);

CREATE INDEX `IX_StockMovements_PartId_Timestamp` ON `StockMovements` (`PartId`, `Timestamp`);

CREATE INDEX `IX_StockMovements_PartNo_Timestamp` ON `StockMovements` (`PartNo`, `Timestamp`);

CREATE INDEX `IX_StockMovements_PartUnitId` ON `StockMovements` (`PartUnitId`);

CREATE INDEX `IX_StockTransfers_PartId` ON `StockTransfers` (`PartId`);

CREATE INDEX `IX_TicketAttachments_TicketId` ON `TicketAttachments` (`TicketId`);

CREATE INDEX `IX_TicketAttachments_WithdrawBatchId` ON `TicketAttachments` (`WithdrawBatchId`);

CREATE INDEX `IX_TicketPartLines_PartId` ON `TicketPartLines` (`PartId`);

CREATE INDEX `IX_TicketPartLines_TicketId` ON `TicketPartLines` (`TicketId`);

CREATE INDEX `IX_TicketPartLines_WithdrawBatchId` ON `TicketPartLines` (`WithdrawBatchId`);

CREATE UNIQUE INDEX `IX_Tickets_ExternalTicketNo` ON `Tickets` (`ExternalTicketNo`);

CREATE UNIQUE INDEX `IX_Users_Email` ON `Users` (`Email`);

CREATE INDEX `IX_WithdrawBatches_TicketId` ON `WithdrawBatches` (`TicketId`);

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260914090437_InitialCreate', '8.0.11');

COMMIT;

