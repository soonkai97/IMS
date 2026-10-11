/*
  10-schema.sql -- reduced but faithful copy of the hot tables of the product (AppDbContext, migration AddDocumentReversedAt): same columns,
  lengths, audit columns, foreign keys, filtered unique indexes (REC_DELETED = 0), FK indexes, hot-path indexes and append-only triggers.
  Only the key strategy changes, through the sqlcmd variables of variants/<Variant>.sqlcmd:
    $(KT)  type of every int-class key and of every foreign key to it (int | uniqueidentifier); also the user columns REC_CREATOR, REC_REVISOR and the *By columns
    $(KL)  type of the ledger and audit keys (bigint | uniqueidentifier)
    $(KTGEN), $(KLGEN)  IDENTITY(1,1) or a DEFAULT expression that generates the GUID
    $(PUBCOL)  extra public uniqueidentifier column (hybrid B) or nothing
  Not copied: UomConversion, UserWarehouse, RefreshToken, Attachment, ApprovalRule, SystemSetting, DocumentSequence (small or off the hot path).
*/
SET NOCOUNT ON; SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;
IF DB_NAME() NOT LIKE N'Ims[_]PerfGuid%' THROW 50000, N'Scratch databases only.', 1;

------------------------------------------------------------------ small reference tables
CREATE TABLE dbo.Uom (
    Id $(KT) NOT NULL $(KTGEN), Code nvarchar(20) NOT NULL, Name nvarchar(100) NOT NULL,
    REC_DELETED bit NOT NULL DEFAULT (0), REC_CREATOR $(KT) NULL, REC_CREDATE datetime2 NOT NULL DEFAULT (SYSUTCDATETIME()), REC_REVISOR $(KT) NULL, REC_REVDATE datetime2 NULL, ROW_VERSION rowversion NOT NULL,
    CONSTRAINT PK_Uom PRIMARY KEY CLUSTERED (Id));
CREATE UNIQUE INDEX IX_Uom_Code ON dbo.Uom (Code) WHERE REC_DELETED = 0;

CREATE TABLE dbo.Category (
    Id $(KT) NOT NULL $(KTGEN), Name nvarchar(200) NOT NULL,
    REC_DELETED bit NOT NULL DEFAULT (0), REC_CREATOR $(KT) NULL, REC_CREDATE datetime2 NOT NULL DEFAULT (SYSUTCDATETIME()), REC_REVISOR $(KT) NULL, REC_REVDATE datetime2 NULL, ROW_VERSION rowversion NOT NULL,
    CONSTRAINT PK_Category PRIMARY KEY CLUSTERED (Id));
CREATE UNIQUE INDEX IX_Category_Name ON dbo.Category (Name) WHERE REC_DELETED = 0;

CREATE TABLE dbo.Warehouse (
    Id $(KT) NOT NULL $(KTGEN), Code nvarchar(20) NOT NULL, Name nvarchar(200) NOT NULL, Contact nvarchar(max) NULL, Notes nvarchar(max) NULL, IsActive bit NOT NULL,
    REC_DELETED bit NOT NULL DEFAULT (0), REC_CREATOR $(KT) NULL, REC_CREDATE datetime2 NOT NULL DEFAULT (SYSUTCDATETIME()), REC_REVISOR $(KT) NULL, REC_REVDATE datetime2 NULL, ROW_VERSION rowversion NOT NULL,
    CONSTRAINT PK_Warehouse PRIMARY KEY CLUSTERED (Id));
CREATE UNIQUE INDEX IX_Warehouse_Code ON dbo.Warehouse (Code) WHERE REC_DELETED = 0;

CREATE TABLE dbo.Partner (
    Id $(KT) NOT NULL $(KTGEN), Type int NOT NULL, Name nvarchar(300) NOT NULL, Contact nvarchar(max) NULL, IsActive bit NOT NULL,
    REC_DELETED bit NOT NULL DEFAULT (0), REC_CREATOR $(KT) NULL, REC_CREDATE datetime2 NOT NULL DEFAULT (SYSUTCDATETIME()), REC_REVISOR $(KT) NULL, REC_REVDATE datetime2 NULL, ROW_VERSION rowversion NOT NULL,
    CONSTRAINT PK_Partner PRIMARY KEY CLUSTERED (Id));
CREATE UNIQUE INDEX IX_Partner_Type_Name ON dbo.Partner (Type, Name) WHERE REC_DELETED = 0;

CREATE TABLE dbo.[User] (
    Id $(KT) NOT NULL $(KTGEN), Username nvarchar(100) NOT NULL, Email nvarchar(256) NOT NULL, DisplayName nvarchar(200) NOT NULL, PasswordHash nvarchar(max) NOT NULL,
    Role nvarchar(32) NOT NULL, IsActive bit NOT NULL, FailedLoginCount int NOT NULL, LockoutUntil datetime2 NULL,
    REC_DELETED bit NOT NULL DEFAULT (0), REC_CREATOR $(KT) NULL, REC_CREDATE datetime2 NOT NULL DEFAULT (SYSUTCDATETIME()), REC_REVISOR $(KT) NULL, REC_REVDATE datetime2 NULL, ROW_VERSION rowversion NOT NULL,
    CONSTRAINT PK_User PRIMARY KEY CLUSTERED (Id));
CREATE UNIQUE INDEX IX_User_Username ON dbo.[User] (Username) WHERE REC_DELETED = 0;
CREATE UNIQUE INDEX IX_User_Email ON dbo.[User] (Email) WHERE REC_DELETED = 0;

------------------------------------------------------------------ master data
CREATE TABLE dbo.Product (
    Id $(KT) NOT NULL $(KTGEN)$(PUBCOL),
    Sku nvarchar(64) NOT NULL, Name nvarchar(300) NOT NULL, CategoryId $(KT) NULL, BaseUomId $(KT) NOT NULL, SafetyStock decimal(18,4) NOT NULL, IsActive bit NOT NULL,
    REC_DELETED bit NOT NULL DEFAULT (0), REC_CREATOR $(KT) NULL, REC_CREDATE datetime2 NOT NULL DEFAULT (SYSUTCDATETIME()), REC_REVISOR $(KT) NULL, REC_REVDATE datetime2 NULL, ROW_VERSION rowversion NOT NULL,
    CONSTRAINT PK_Product PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT FK_Product_Category FOREIGN KEY (CategoryId) REFERENCES dbo.Category (Id),
    CONSTRAINT FK_Product_Uom FOREIGN KEY (BaseUomId) REFERENCES dbo.Uom (Id));
CREATE UNIQUE INDEX IX_Product_Sku ON dbo.Product (Sku) WHERE REC_DELETED = 0;
CREATE INDEX IX_Product_Name ON dbo.Product (Name) INCLUDE (Sku, IsActive) WHERE REC_DELETED = 0;
CREATE INDEX IX_Product_CategoryId ON dbo.Product (CategoryId);
CREATE INDEX IX_Product_BaseUomId ON dbo.Product (BaseUomId);

CREATE TABLE dbo.ProductBarcode (
    Id $(KT) NOT NULL $(KTGEN), ProductId $(KT) NOT NULL, Barcode nvarchar(64) NOT NULL,
    REC_DELETED bit NOT NULL DEFAULT (0), REC_CREATOR $(KT) NULL, REC_CREDATE datetime2 NOT NULL DEFAULT (SYSUTCDATETIME()), REC_REVISOR $(KT) NULL, REC_REVDATE datetime2 NULL, ROW_VERSION rowversion NOT NULL,
    CONSTRAINT PK_ProductBarcode PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT FK_ProductBarcode_Product FOREIGN KEY (ProductId) REFERENCES dbo.Product (Id) ON DELETE CASCADE);
CREATE UNIQUE INDEX IX_ProductBarcode_Barcode ON dbo.ProductBarcode (Barcode) WHERE REC_DELETED = 0;
CREATE INDEX IX_ProductBarcode_ProductId ON dbo.ProductBarcode (ProductId);

------------------------------------------------------------------ documents
CREATE TABLE dbo.StockDocument (
    Id $(KT) NOT NULL $(KTGEN)$(PUBCOL),
    Type int NOT NULL, No nvarchar(40) NOT NULL, Status int NOT NULL, WarehouseId $(KT) NOT NULL, PartnerId $(KT) NULL, Purpose int NULL, Remark nvarchar(500) NULL,
    CreatedBy $(KT) NOT NULL, CreatedAt datetime2 NOT NULL, SubmittedBy $(KT) NULL, SubmittedAt datetime2 NULL, ApprovedBy $(KT) NULL, ApprovedAt datetime2 NULL,
    PostedBy $(KT) NULL, PostedAt datetime2 NULL, RequiresApproval bit NOT NULL, RejectionReason nvarchar(max) NULL, CancelReason nvarchar(max) NULL, ReversalReason nvarchar(max) NULL,
    ReversesDocumentId $(KT) NULL, NegativeStockOverrideReason nvarchar(max) NULL, IsLarge bit NOT NULL DEFAULT (0), RejectedAt datetime2 NULL, RejectedBy $(KT) NULL,
    RequiresOverride bit NOT NULL DEFAULT (0), ReversedAt datetime2 NULL,
    REC_DELETED bit NOT NULL DEFAULT (0), REC_CREATOR $(KT) NULL, REC_CREDATE datetime2 NOT NULL DEFAULT (SYSUTCDATETIME()), REC_REVISOR $(KT) NULL, REC_REVDATE datetime2 NULL, ROW_VERSION rowversion NOT NULL,
    CONSTRAINT PK_StockDocument PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT FK_StockDocument_Warehouse FOREIGN KEY (WarehouseId) REFERENCES dbo.Warehouse (Id),
    CONSTRAINT FK_StockDocument_Partner FOREIGN KEY (PartnerId) REFERENCES dbo.Partner (Id),
    CONSTRAINT FK_StockDocument_CreatedBy FOREIGN KEY (CreatedBy) REFERENCES dbo.[User] (Id),
    CONSTRAINT FK_StockDocument_SubmittedBy FOREIGN KEY (SubmittedBy) REFERENCES dbo.[User] (Id),
    CONSTRAINT FK_StockDocument_ApprovedBy FOREIGN KEY (ApprovedBy) REFERENCES dbo.[User] (Id),
    CONSTRAINT FK_StockDocument_PostedBy FOREIGN KEY (PostedBy) REFERENCES dbo.[User] (Id),
    CONSTRAINT FK_StockDocument_RejectedBy FOREIGN KEY (RejectedBy) REFERENCES dbo.[User] (Id));
CREATE UNIQUE INDEX IX_StockDocument_No ON dbo.StockDocument (No) WHERE REC_DELETED = 0;
CREATE INDEX IX_StockDocument_Status_Type_Id ON dbo.StockDocument (Status, Type, Id DESC) INCLUDE (WarehouseId) WHERE REC_DELETED = 0;
CREATE INDEX IX_StockDocument_CreatedAt ON dbo.StockDocument (CreatedAt) WHERE REC_DELETED = 0;
CREATE INDEX IX_StockDocument_PostedAt ON dbo.StockDocument (PostedAt) INCLUDE (Status, WarehouseId) WHERE REC_DELETED = 0 AND PostedAt IS NOT NULL;
CREATE INDEX IX_StockDocument_ReversedAt ON dbo.StockDocument (ReversedAt) INCLUDE (Status, Type, WarehouseId, PartnerId, No) WHERE REC_DELETED = 0 AND ReversedAt IS NOT NULL;
CREATE INDEX IX_StockDocument_WarehouseId ON dbo.StockDocument (WarehouseId);
CREATE INDEX IX_StockDocument_PartnerId ON dbo.StockDocument (PartnerId);
CREATE INDEX IX_StockDocument_CreatedBy ON dbo.StockDocument (CreatedBy);
CREATE INDEX IX_StockDocument_SubmittedBy ON dbo.StockDocument (SubmittedBy);
CREATE INDEX IX_StockDocument_ApprovedBy ON dbo.StockDocument (ApprovedBy);
CREATE INDEX IX_StockDocument_PostedBy ON dbo.StockDocument (PostedBy);
CREATE INDEX IX_StockDocument_RejectedBy ON dbo.StockDocument (RejectedBy);

CREATE TABLE dbo.StockDocumentLine (
    Id $(KT) NOT NULL $(KTGEN)$(PUBCOL),
    DocumentId $(KT) NOT NULL, ProductId $(KT) NOT NULL, Qty decimal(18,4) NOT NULL, UomId $(KT) NOT NULL, BaseQty decimal(18,4) NOT NULL, UnitCost decimal(18,6) NOT NULL, Remark nvarchar(500) NULL,
    REC_DELETED bit NOT NULL DEFAULT (0), REC_CREATOR $(KT) NULL, REC_CREDATE datetime2 NOT NULL DEFAULT (SYSUTCDATETIME()), REC_REVISOR $(KT) NULL, REC_REVDATE datetime2 NULL, ROW_VERSION rowversion NOT NULL,
    CONSTRAINT PK_StockDocumentLine PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT FK_StockDocumentLine_Document FOREIGN KEY (DocumentId) REFERENCES dbo.StockDocument (Id) ON DELETE CASCADE,
    CONSTRAINT FK_StockDocumentLine_Product FOREIGN KEY (ProductId) REFERENCES dbo.Product (Id),
    CONSTRAINT FK_StockDocumentLine_Uom FOREIGN KEY (UomId) REFERENCES dbo.Uom (Id));
CREATE INDEX IX_StockDocumentLine_DocumentId ON dbo.StockDocumentLine (DocumentId);
CREATE INDEX IX_StockDocumentLine_DocumentId_Live ON dbo.StockDocumentLine (DocumentId) WHERE REC_DELETED = 0;
CREATE INDEX IX_StockDocumentLine_ProductId ON dbo.StockDocumentLine (ProductId);
CREATE INDEX IX_StockDocumentLine_UomId ON dbo.StockDocumentLine (UomId);

------------------------------------------------------------------ stock: ledger (append-only) and balance
CREATE TABLE dbo.StockLedgerEntry (
    Id $(KL) NOT NULL $(KLGEN)$(PUBCOL),
    ProductId $(KT) NOT NULL, WarehouseId $(KT) NOT NULL, QtyDelta decimal(18,4) NOT NULL, UnitCost decimal(18,6) NOT NULL, SourceDocId $(KT) NOT NULL, Reason int NOT NULL,
    OccurredAt datetime2 NOT NULL, CreatedBy $(KT) NOT NULL,
    REC_DELETED bit NOT NULL DEFAULT (0), REC_CREATOR $(KT) NULL, REC_CREDATE datetime2 NOT NULL DEFAULT (SYSUTCDATETIME()), REC_REVISOR $(KT) NULL, REC_REVDATE datetime2 NULL, ROW_VERSION rowversion NOT NULL,
    CONSTRAINT PK_StockLedgerEntry PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT FK_StockLedgerEntry_Product FOREIGN KEY (ProductId) REFERENCES dbo.Product (Id),
    CONSTRAINT FK_StockLedgerEntry_Warehouse FOREIGN KEY (WarehouseId) REFERENCES dbo.Warehouse (Id),
    CONSTRAINT FK_StockLedgerEntry_Document FOREIGN KEY (SourceDocId) REFERENCES dbo.StockDocument (Id),
    CONSTRAINT FK_StockLedgerEntry_CreatedBy FOREIGN KEY (CreatedBy) REFERENCES dbo.[User] (Id));
CREATE INDEX IX_StockLedgerEntry_ProductId_WarehouseId_OccurredAt ON dbo.StockLedgerEntry (ProductId, WarehouseId, OccurredAt);
CREATE INDEX IX_StockLedgerEntry_SourceDocId ON dbo.StockLedgerEntry (SourceDocId);
CREATE INDEX IX_StockLedgerEntry_OccurredAt ON dbo.StockLedgerEntry (OccurredAt);
CREATE INDEX IX_StockLedgerEntry_WarehouseId ON dbo.StockLedgerEntry (WarehouseId);
CREATE INDEX IX_StockLedgerEntry_CreatedBy ON dbo.StockLedgerEntry (CreatedBy);
GO
CREATE TRIGGER dbo.TR_StockLedgerEntry_AppendOnly ON dbo.StockLedgerEntry INSTEAD OF UPDATE, DELETE AS
BEGIN THROW 50100, N'StockLedgerEntry is append-only.', 1; END
GO

CREATE TABLE dbo.StockBalance (
    ProductId $(KT) NOT NULL, WarehouseId $(KT) NOT NULL, QtyOnHand decimal(18,4) NOT NULL, QtyInTransit decimal(18,4) NOT NULL, AvgCost decimal(18,6) NOT NULL,
    REC_DELETED bit NOT NULL DEFAULT (0), REC_CREATOR $(KT) NULL, REC_CREDATE datetime2 NOT NULL DEFAULT (SYSUTCDATETIME()), REC_REVISOR $(KT) NULL, REC_REVDATE datetime2 NULL, ROW_VERSION rowversion NOT NULL,
    CONSTRAINT PK_StockBalance PRIMARY KEY CLUSTERED (ProductId, WarehouseId),
    CONSTRAINT FK_StockBalance_Product FOREIGN KEY (ProductId) REFERENCES dbo.Product (Id),
    CONSTRAINT FK_StockBalance_Warehouse FOREIGN KEY (WarehouseId) REFERENCES dbo.Warehouse (Id));
CREATE INDEX IX_StockBalance_WarehouseId ON dbo.StockBalance (WarehouseId);

------------------------------------------------------------------ audit log (append-only)
CREATE TABLE dbo.AuditLogs (
    Id $(KL) NOT NULL $(KLGEN)$(PUBCOL),
    Actor nvarchar(100) NOT NULL, Action nvarchar(100) NOT NULL, EntityType nvarchar(100) NOT NULL, EntityId nvarchar(100) NOT NULL, [Before] nvarchar(max) NULL, [After] nvarchar(max) NULL, [At] datetime2 NOT NULL,
    REC_DELETED bit NOT NULL DEFAULT (0), REC_CREATOR $(KT) NULL, REC_CREDATE datetime2 NOT NULL DEFAULT (SYSUTCDATETIME()), REC_REVISOR $(KT) NULL, REC_REVDATE datetime2 NULL, ROW_VERSION rowversion NOT NULL,
    CONSTRAINT PK_AuditLogs PRIMARY KEY CLUSTERED (Id));
CREATE INDEX IX_AuditLogs_At ON dbo.AuditLogs ([At]);
CREATE INDEX IX_AuditLogs_EntityType_EntityId ON dbo.AuditLogs (EntityType, EntityId);
CREATE INDEX IX_AuditLogs_Actor_Id ON dbo.AuditLogs (Actor, Id DESC);
CREATE INDEX IX_AuditLogs_Action_Id ON dbo.AuditLogs (Action, Id DESC);
CREATE INDEX IX_AuditLogs_EntityId_Id ON dbo.AuditLogs (EntityId, Id DESC);
GO
CREATE TRIGGER dbo.TR_AuditLogs_AppendOnly ON dbo.AuditLogs INSTEAD OF UPDATE, DELETE AS
BEGIN THROW 50101, N'AuditLogs is append-only.', 1; END
GO

------------------------------------------------------------------ hybrid B: unique public id (only when $(PUBIDX) = 1)
IF $(PUBIDX) = 1
BEGIN
    EXEC (N'CREATE UNIQUE INDEX UX_Product_PublicId ON dbo.Product (PublicId);
            CREATE UNIQUE INDEX UX_StockDocument_PublicId ON dbo.StockDocument (PublicId);
            CREATE UNIQUE INDEX UX_StockDocumentLine_PublicId ON dbo.StockDocumentLine (PublicId);
            CREATE UNIQUE INDEX UX_StockLedgerEntry_PublicId ON dbo.StockLedgerEntry (PublicId);
            CREATE UNIQUE INDEX UX_AuditLogs_PublicId ON dbo.AuditLogs (PublicId);');
END

------------------------------------------------------------------ lookup helpers for the writers (not part of the measured model; excluded from size accounting)
CREATE TABLE dbo._Pick_Product (n int NOT NULL PRIMARY KEY, ProductId $(KT) NOT NULL, UomId $(KT) NOT NULL);
CREATE TABLE dbo._Pick_Wh (n int NOT NULL PRIMARY KEY, WarehouseId $(KT) NOT NULL);
CREATE TABLE dbo._Pick_User (n int NOT NULL PRIMARY KEY, UserId $(KT) NOT NULL, Username nvarchar(100) NOT NULL);
CREATE TABLE dbo._Pick_Partner (n int NOT NULL PRIMARY KEY, PartnerId $(KT) NOT NULL);
CREATE TABLE dbo._WriterLog (Id int IDENTITY PRIMARY KEY, Tag varchar(60) NOT NULL, Writer int NOT NULL, Tx int NOT NULL, Retries int NOT NULL, StartUtc datetime2 NOT NULL, EndUtc datetime2 NOT NULL);

CREATE TABLE dbo._State (k varchar(30) NOT NULL PRIMARY KEY, v bigint NOT NULL);
INSERT dbo._State VALUES ('ordNext', 0);
