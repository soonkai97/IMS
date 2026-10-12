namespace Ims.Api.Tests.Tests.Schema;

/// <summary>
/// The shape of the database as the migrations build it today (<see cref="SchemaShape"/> writes the same lines). Change it in the pull request
/// that changes a key, a foreign key or a unique index, never to make a red test green. Order does not matter.
/// 19 tables, 27 foreign keys, 13 unique indexes besides the primary keys, 2 append-only triggers.
/// </summary>
public static class SchemaPin
{
    public const string Expected = """
        TABLE ApprovalRule PK(Id:int identity) clustered
        TABLE Attachment PK(Id:int identity) clustered
        TABLE AuditLogs PK(Id:bigint identity) clustered
        TABLE Category PK(Id:int identity) clustered
        TABLE DocumentSequence PK(Prefix:nvarchar, YearMonth:int, WarehouseId:int) clustered
        TABLE Partner PK(Id:int identity) clustered
        TABLE Product PK(Id:int identity) clustered
        TABLE ProductBarcode PK(Id:int identity) clustered
        TABLE RefreshToken PK(Id:int identity) clustered
        TABLE StockBalance PK(ProductId:int, WarehouseId:int) clustered
        TABLE StockDocument PK(Id:int identity) clustered
        TABLE StockDocumentLine PK(Id:int identity) clustered
        TABLE StockLedgerEntry PK(Id:bigint identity) clustered
        TABLE SystemSetting PK(Key:nvarchar) clustered
        TABLE Uom PK(Id:int identity) clustered
        TABLE UomConversion PK(Id:int identity) clustered
        TABLE User PK(Id:int identity) clustered
        TABLE UserWarehouse PK(Id:int identity) clustered
        TABLE Warehouse PK(Id:int identity) clustered
        FK Attachment(UploadedBy) -> User(Id) on delete no_action
        FK Product(BaseUomId) -> Uom(Id) on delete no_action
        FK Product(CategoryId) -> Category(Id) on delete no_action
        FK ProductBarcode(ProductId) -> Product(Id) on delete cascade
        FK RefreshToken(UserId) -> User(Id) on delete cascade
        FK StockBalance(ProductId) -> Product(Id) on delete no_action
        FK StockBalance(WarehouseId) -> Warehouse(Id) on delete no_action
        FK StockDocument(ApprovedBy) -> User(Id) on delete no_action
        FK StockDocument(CreatedBy) -> User(Id) on delete no_action
        FK StockDocument(PartnerId) -> Partner(Id) on delete no_action
        FK StockDocument(PostedBy) -> User(Id) on delete no_action
        FK StockDocument(RejectedBy) -> User(Id) on delete no_action
        FK StockDocument(ReversedBy) -> User(Id) on delete no_action
        FK StockDocument(SubmittedBy) -> User(Id) on delete no_action
        FK StockDocument(WarehouseId) -> Warehouse(Id) on delete no_action
        FK StockDocumentLine(DocumentId) -> StockDocument(Id) on delete cascade
        FK StockDocumentLine(ProductId) -> Product(Id) on delete no_action
        FK StockDocumentLine(UomId) -> Uom(Id) on delete no_action
        FK StockLedgerEntry(CreatedBy) -> User(Id) on delete no_action
        FK StockLedgerEntry(ProductId) -> Product(Id) on delete no_action
        FK StockLedgerEntry(SourceDocId) -> StockDocument(Id) on delete no_action
        FK StockLedgerEntry(WarehouseId) -> Warehouse(Id) on delete no_action
        FK UomConversion(FromUomId) -> Uom(Id) on delete no_action
        FK UomConversion(ProductId) -> Product(Id) on delete cascade
        FK UomConversion(ToUomId) -> Uom(Id) on delete no_action
        FK UserWarehouse(UserId) -> User(Id) on delete cascade
        FK UserWarehouse(WarehouseId) -> Warehouse(Id) on delete no_action
        UNIQUE Attachment.IX_Attachment_StoragePath (StoragePath) where ([REC_DELETED]=(0))
        UNIQUE Category.IX_Category_Name (Name) where ([REC_DELETED]=(0))
        UNIQUE Partner.IX_Partner_Type_Name (Type, Name) where ([REC_DELETED]=(0))
        UNIQUE Product.IX_Product_Sku (Sku) where ([REC_DELETED]=(0))
        UNIQUE ProductBarcode.IX_ProductBarcode_Barcode (Barcode) where ([REC_DELETED]=(0))
        UNIQUE RefreshToken.IX_RefreshToken_TokenHash (TokenHash) where ([REC_DELETED]=(0))
        UNIQUE StockDocument.IX_StockDocument_No (No) where ([REC_DELETED]=(0))
        UNIQUE Uom.IX_Uom_Code (Code) where ([REC_DELETED]=(0))
        UNIQUE UomConversion.IX_UomConversion_ProductId_FromUomId_ToUomId (ProductId, FromUomId, ToUomId) where ([REC_DELETED]=(0))
        UNIQUE User.IX_User_Email (Email) where ([REC_DELETED]=(0))
        UNIQUE User.IX_User_Username (Username) where ([REC_DELETED]=(0))
        UNIQUE UserWarehouse.IX_UserWarehouse_UserId_WarehouseId (UserId, WarehouseId) where ([REC_DELETED]=(0))
        UNIQUE Warehouse.IX_Warehouse_Code (Code) where ([REC_DELETED]=(0))
        TRIGGER AuditLogs.TR_AuditLogs_AppendOnly
        TRIGGER StockLedgerEntry.TR_StockLedgerEntry_AppendOnly
        READ_COMMITTED_SNAPSHOT on
        """;
}
