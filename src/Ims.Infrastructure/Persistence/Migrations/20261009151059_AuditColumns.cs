using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ims.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AuditColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Warehouse_Code",
                table: "Warehouse");

            migrationBuilder.DropPrimaryKey(
                name: "PK_UserWarehouse",
                table: "UserWarehouse");

            migrationBuilder.DropIndex(
                name: "IX_User_Email",
                table: "User");

            migrationBuilder.DropIndex(
                name: "IX_User_Username",
                table: "User");

            migrationBuilder.DropIndex(
                name: "IX_UomConversion_ProductId_FromUomId_ToUomId",
                table: "UomConversion");

            migrationBuilder.DropIndex(
                name: "IX_Uom_Code",
                table: "Uom");

            migrationBuilder.DropIndex(
                name: "IX_StockDocument_No",
                table: "StockDocument");

            migrationBuilder.DropIndex(
                name: "IX_RefreshToken_TokenHash",
                table: "RefreshToken");

            migrationBuilder.DropIndex(
                name: "IX_ProductBarcode_Barcode",
                table: "ProductBarcode");

            migrationBuilder.DropIndex(
                name: "IX_Product_Sku",
                table: "Product");

            migrationBuilder.DropIndex(
                name: "IX_Category_Name",
                table: "Category");

            migrationBuilder.DropIndex(
                name: "IX_Attachment_OwnerType_OwnerId",
                table: "Attachment");

            migrationBuilder.DropIndex(
                name: "IX_Attachment_StoragePath",
                table: "Attachment");

            migrationBuilder.RenameColumn(
                name: "RowVersion",
                table: "User",
                newName: "ROW_VERSION");

            migrationBuilder.RenameColumn(
                name: "RowVersion",
                table: "StockDocument",
                newName: "ROW_VERSION");

            migrationBuilder.RenameColumn(
                name: "RowVersion",
                table: "StockBalance",
                newName: "ROW_VERSION");

            migrationBuilder.RenameColumn(
                name: "RowVersion",
                table: "RefreshToken",
                newName: "ROW_VERSION");

            migrationBuilder.RenameColumn(
                name: "RowVersion",
                table: "DocumentSequence",
                newName: "ROW_VERSION");

            migrationBuilder.RenameColumn(
                name: "DeletedAt",
                table: "Attachment",
                newName: "REC_REVDATE");

            migrationBuilder.AddColumn<int>(
                name: "REC_CREATOR",
                table: "Warehouse",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "REC_CREDATE",
                table: "Warehouse",
                type: "datetime2",
                nullable: false,
                defaultValueSql: "SYSUTCDATETIME()");

            migrationBuilder.AddColumn<bool>(
                name: "REC_DELETED",
                table: "Warehouse",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "REC_REVDATE",
                table: "Warehouse",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "REC_REVISOR",
                table: "Warehouse",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "ROW_VERSION",
                table: "Warehouse",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddColumn<int>(
                name: "Id",
                table: "UserWarehouse",
                type: "int",
                nullable: false,
                defaultValue: 0)
                .Annotation("SqlServer:Identity", "1, 1");

            migrationBuilder.AddColumn<int>(
                name: "REC_CREATOR",
                table: "UserWarehouse",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "REC_CREDATE",
                table: "UserWarehouse",
                type: "datetime2",
                nullable: false,
                defaultValueSql: "SYSUTCDATETIME()");

            migrationBuilder.AddColumn<bool>(
                name: "REC_DELETED",
                table: "UserWarehouse",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "REC_REVDATE",
                table: "UserWarehouse",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "REC_REVISOR",
                table: "UserWarehouse",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "ROW_VERSION",
                table: "UserWarehouse",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddColumn<int>(
                name: "REC_CREATOR",
                table: "User",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "REC_CREDATE",
                table: "User",
                type: "datetime2",
                nullable: false,
                defaultValueSql: "SYSUTCDATETIME()");

            migrationBuilder.AddColumn<bool>(
                name: "REC_DELETED",
                table: "User",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "REC_REVDATE",
                table: "User",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "REC_REVISOR",
                table: "User",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "REC_CREATOR",
                table: "UomConversion",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "REC_CREDATE",
                table: "UomConversion",
                type: "datetime2",
                nullable: false,
                defaultValueSql: "SYSUTCDATETIME()");

            migrationBuilder.AddColumn<bool>(
                name: "REC_DELETED",
                table: "UomConversion",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "REC_REVDATE",
                table: "UomConversion",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "REC_REVISOR",
                table: "UomConversion",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "ROW_VERSION",
                table: "UomConversion",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddColumn<int>(
                name: "REC_CREATOR",
                table: "Uom",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "REC_CREDATE",
                table: "Uom",
                type: "datetime2",
                nullable: false,
                defaultValueSql: "SYSUTCDATETIME()");

            migrationBuilder.AddColumn<bool>(
                name: "REC_DELETED",
                table: "Uom",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "REC_REVDATE",
                table: "Uom",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "REC_REVISOR",
                table: "Uom",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "ROW_VERSION",
                table: "Uom",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddColumn<int>(
                name: "REC_CREATOR",
                table: "SystemSetting",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "REC_CREDATE",
                table: "SystemSetting",
                type: "datetime2",
                nullable: false,
                defaultValueSql: "SYSUTCDATETIME()");

            migrationBuilder.AddColumn<bool>(
                name: "REC_DELETED",
                table: "SystemSetting",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "REC_REVDATE",
                table: "SystemSetting",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "REC_REVISOR",
                table: "SystemSetting",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "ROW_VERSION",
                table: "SystemSetting",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddColumn<int>(
                name: "REC_CREATOR",
                table: "StockLedgerEntry",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "REC_CREDATE",
                table: "StockLedgerEntry",
                type: "datetime2",
                nullable: false,
                defaultValueSql: "SYSUTCDATETIME()");

            migrationBuilder.AddColumn<bool>(
                name: "REC_DELETED",
                table: "StockLedgerEntry",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "REC_REVDATE",
                table: "StockLedgerEntry",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "REC_REVISOR",
                table: "StockLedgerEntry",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "ROW_VERSION",
                table: "StockLedgerEntry",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddColumn<int>(
                name: "REC_CREATOR",
                table: "StockDocumentLine",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "REC_CREDATE",
                table: "StockDocumentLine",
                type: "datetime2",
                nullable: false,
                defaultValueSql: "SYSUTCDATETIME()");

            migrationBuilder.AddColumn<bool>(
                name: "REC_DELETED",
                table: "StockDocumentLine",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "REC_REVDATE",
                table: "StockDocumentLine",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "REC_REVISOR",
                table: "StockDocumentLine",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "ROW_VERSION",
                table: "StockDocumentLine",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddColumn<int>(
                name: "REC_CREATOR",
                table: "StockDocument",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "REC_CREDATE",
                table: "StockDocument",
                type: "datetime2",
                nullable: false,
                defaultValueSql: "SYSUTCDATETIME()");

            migrationBuilder.AddColumn<bool>(
                name: "REC_DELETED",
                table: "StockDocument",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "REC_REVDATE",
                table: "StockDocument",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "REC_REVISOR",
                table: "StockDocument",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "REC_CREATOR",
                table: "StockBalance",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "REC_CREDATE",
                table: "StockBalance",
                type: "datetime2",
                nullable: false,
                defaultValueSql: "SYSUTCDATETIME()");

            migrationBuilder.AddColumn<bool>(
                name: "REC_DELETED",
                table: "StockBalance",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "REC_REVDATE",
                table: "StockBalance",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "REC_REVISOR",
                table: "StockBalance",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "REC_CREATOR",
                table: "RefreshToken",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "REC_CREDATE",
                table: "RefreshToken",
                type: "datetime2",
                nullable: false,
                defaultValueSql: "SYSUTCDATETIME()");

            migrationBuilder.AddColumn<bool>(
                name: "REC_DELETED",
                table: "RefreshToken",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "REC_REVDATE",
                table: "RefreshToken",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "REC_REVISOR",
                table: "RefreshToken",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "REC_CREATOR",
                table: "ProductBarcode",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "REC_CREDATE",
                table: "ProductBarcode",
                type: "datetime2",
                nullable: false,
                defaultValueSql: "SYSUTCDATETIME()");

            migrationBuilder.AddColumn<bool>(
                name: "REC_DELETED",
                table: "ProductBarcode",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "REC_REVDATE",
                table: "ProductBarcode",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "REC_REVISOR",
                table: "ProductBarcode",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "ROW_VERSION",
                table: "ProductBarcode",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddColumn<int>(
                name: "REC_CREATOR",
                table: "Product",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "REC_CREDATE",
                table: "Product",
                type: "datetime2",
                nullable: false,
                defaultValueSql: "SYSUTCDATETIME()");

            migrationBuilder.AddColumn<bool>(
                name: "REC_DELETED",
                table: "Product",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "REC_REVDATE",
                table: "Product",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "REC_REVISOR",
                table: "Product",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "ROW_VERSION",
                table: "Product",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddColumn<int>(
                name: "REC_CREATOR",
                table: "Partner",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "REC_CREDATE",
                table: "Partner",
                type: "datetime2",
                nullable: false,
                defaultValueSql: "SYSUTCDATETIME()");

            migrationBuilder.AddColumn<bool>(
                name: "REC_DELETED",
                table: "Partner",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "REC_REVDATE",
                table: "Partner",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "REC_REVISOR",
                table: "Partner",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "ROW_VERSION",
                table: "Partner",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddColumn<int>(
                name: "REC_CREATOR",
                table: "DocumentSequence",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "REC_CREDATE",
                table: "DocumentSequence",
                type: "datetime2",
                nullable: false,
                defaultValueSql: "SYSUTCDATETIME()");

            migrationBuilder.AddColumn<bool>(
                name: "REC_DELETED",
                table: "DocumentSequence",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "REC_REVDATE",
                table: "DocumentSequence",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "REC_REVISOR",
                table: "DocumentSequence",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "REC_CREATOR",
                table: "Category",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "REC_CREDATE",
                table: "Category",
                type: "datetime2",
                nullable: false,
                defaultValueSql: "SYSUTCDATETIME()");

            migrationBuilder.AddColumn<bool>(
                name: "REC_DELETED",
                table: "Category",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "REC_REVDATE",
                table: "Category",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "REC_REVISOR",
                table: "Category",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "ROW_VERSION",
                table: "Category",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddColumn<int>(
                name: "REC_CREATOR",
                table: "AuditLogs",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "REC_CREDATE",
                table: "AuditLogs",
                type: "datetime2",
                nullable: false,
                defaultValueSql: "SYSUTCDATETIME()");

            migrationBuilder.AddColumn<bool>(
                name: "REC_DELETED",
                table: "AuditLogs",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "REC_REVDATE",
                table: "AuditLogs",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "REC_REVISOR",
                table: "AuditLogs",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "ROW_VERSION",
                table: "AuditLogs",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddColumn<int>(
                name: "REC_CREATOR",
                table: "Attachment",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "REC_CREDATE",
                table: "Attachment",
                type: "datetime2",
                nullable: false,
                defaultValueSql: "SYSUTCDATETIME()");

            migrationBuilder.AddColumn<bool>(
                name: "REC_DELETED",
                table: "Attachment",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "REC_REVISOR",
                table: "Attachment",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "ROW_VERSION",
                table: "Attachment",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddColumn<int>(
                name: "REC_CREATOR",
                table: "ApprovalRule",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "REC_CREDATE",
                table: "ApprovalRule",
                type: "datetime2",
                nullable: false,
                defaultValueSql: "SYSUTCDATETIME()");

            migrationBuilder.AddColumn<bool>(
                name: "REC_DELETED",
                table: "ApprovalRule",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "REC_REVDATE",
                table: "ApprovalRule",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "REC_REVISOR",
                table: "ApprovalRule",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "ROW_VERSION",
                table: "ApprovalRule",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddPrimaryKey(
                name: "PK_UserWarehouse",
                table: "UserWarehouse",
                column: "Id");

            migrationBuilder.CreateIndex(
                name: "IX_Warehouse_Code",
                table: "Warehouse",
                column: "Code",
                unique: true,
                filter: "[REC_DELETED] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_UserWarehouse_UserId_WarehouseId",
                table: "UserWarehouse",
                columns: new[] { "UserId", "WarehouseId" },
                unique: true,
                filter: "[REC_DELETED] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_User_Email",
                table: "User",
                column: "Email",
                unique: true,
                filter: "[REC_DELETED] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_User_Username",
                table: "User",
                column: "Username",
                unique: true,
                filter: "[REC_DELETED] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_UomConversion_ProductId_FromUomId_ToUomId",
                table: "UomConversion",
                columns: new[] { "ProductId", "FromUomId", "ToUomId" },
                unique: true,
                filter: "[REC_DELETED] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_Uom_Code",
                table: "Uom",
                column: "Code",
                unique: true,
                filter: "[REC_DELETED] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_StockDocument_No",
                table: "StockDocument",
                column: "No",
                unique: true,
                filter: "[REC_DELETED] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_RefreshToken_TokenHash",
                table: "RefreshToken",
                column: "TokenHash",
                unique: true,
                filter: "[REC_DELETED] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_ProductBarcode_Barcode",
                table: "ProductBarcode",
                column: "Barcode",
                unique: true,
                filter: "[REC_DELETED] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_Product_Sku",
                table: "Product",
                column: "Sku",
                unique: true,
                filter: "[REC_DELETED] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_Category_Name",
                table: "Category",
                column: "Name",
                unique: true,
                filter: "[REC_DELETED] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_Attachment_OwnerType_OwnerId",
                table: "Attachment",
                columns: new[] { "OwnerType", "OwnerId" },
                filter: "[REC_DELETED] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_Attachment_StoragePath",
                table: "Attachment",
                column: "StoragePath",
                unique: true,
                filter: "[REC_DELETED] = 0");

            // Attachments removed before soft delete became the standard (their DeletedAt is now REC_REVDATE) stay deleted.
            migrationBuilder.Sql("UPDATE [Attachment] SET [REC_DELETED] = 1 WHERE [REC_REVDATE] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Warehouse_Code",
                table: "Warehouse");

            migrationBuilder.DropPrimaryKey(
                name: "PK_UserWarehouse",
                table: "UserWarehouse");

            migrationBuilder.DropIndex(
                name: "IX_UserWarehouse_UserId_WarehouseId",
                table: "UserWarehouse");

            migrationBuilder.DropIndex(
                name: "IX_User_Email",
                table: "User");

            migrationBuilder.DropIndex(
                name: "IX_User_Username",
                table: "User");

            migrationBuilder.DropIndex(
                name: "IX_UomConversion_ProductId_FromUomId_ToUomId",
                table: "UomConversion");

            migrationBuilder.DropIndex(
                name: "IX_Uom_Code",
                table: "Uom");

            migrationBuilder.DropIndex(
                name: "IX_StockDocument_No",
                table: "StockDocument");

            migrationBuilder.DropIndex(
                name: "IX_RefreshToken_TokenHash",
                table: "RefreshToken");

            migrationBuilder.DropIndex(
                name: "IX_ProductBarcode_Barcode",
                table: "ProductBarcode");

            migrationBuilder.DropIndex(
                name: "IX_Product_Sku",
                table: "Product");

            migrationBuilder.DropIndex(
                name: "IX_Category_Name",
                table: "Category");

            migrationBuilder.DropIndex(
                name: "IX_Attachment_OwnerType_OwnerId",
                table: "Attachment");

            migrationBuilder.DropIndex(
                name: "IX_Attachment_StoragePath",
                table: "Attachment");

            migrationBuilder.DropColumn(
                name: "REC_CREATOR",
                table: "Warehouse");

            migrationBuilder.DropColumn(
                name: "REC_CREDATE",
                table: "Warehouse");

            migrationBuilder.DropColumn(
                name: "REC_DELETED",
                table: "Warehouse");

            migrationBuilder.DropColumn(
                name: "REC_REVDATE",
                table: "Warehouse");

            migrationBuilder.DropColumn(
                name: "REC_REVISOR",
                table: "Warehouse");

            migrationBuilder.DropColumn(
                name: "ROW_VERSION",
                table: "Warehouse");

            migrationBuilder.DropColumn(
                name: "Id",
                table: "UserWarehouse");

            migrationBuilder.DropColumn(
                name: "REC_CREATOR",
                table: "UserWarehouse");

            migrationBuilder.DropColumn(
                name: "REC_CREDATE",
                table: "UserWarehouse");

            migrationBuilder.DropColumn(
                name: "REC_DELETED",
                table: "UserWarehouse");

            migrationBuilder.DropColumn(
                name: "REC_REVDATE",
                table: "UserWarehouse");

            migrationBuilder.DropColumn(
                name: "REC_REVISOR",
                table: "UserWarehouse");

            migrationBuilder.DropColumn(
                name: "ROW_VERSION",
                table: "UserWarehouse");

            migrationBuilder.DropColumn(
                name: "REC_CREATOR",
                table: "User");

            migrationBuilder.DropColumn(
                name: "REC_CREDATE",
                table: "User");

            migrationBuilder.DropColumn(
                name: "REC_DELETED",
                table: "User");

            migrationBuilder.DropColumn(
                name: "REC_REVDATE",
                table: "User");

            migrationBuilder.DropColumn(
                name: "REC_REVISOR",
                table: "User");

            migrationBuilder.DropColumn(
                name: "REC_CREATOR",
                table: "UomConversion");

            migrationBuilder.DropColumn(
                name: "REC_CREDATE",
                table: "UomConversion");

            migrationBuilder.DropColumn(
                name: "REC_DELETED",
                table: "UomConversion");

            migrationBuilder.DropColumn(
                name: "REC_REVDATE",
                table: "UomConversion");

            migrationBuilder.DropColumn(
                name: "REC_REVISOR",
                table: "UomConversion");

            migrationBuilder.DropColumn(
                name: "ROW_VERSION",
                table: "UomConversion");

            migrationBuilder.DropColumn(
                name: "REC_CREATOR",
                table: "Uom");

            migrationBuilder.DropColumn(
                name: "REC_CREDATE",
                table: "Uom");

            migrationBuilder.DropColumn(
                name: "REC_DELETED",
                table: "Uom");

            migrationBuilder.DropColumn(
                name: "REC_REVDATE",
                table: "Uom");

            migrationBuilder.DropColumn(
                name: "REC_REVISOR",
                table: "Uom");

            migrationBuilder.DropColumn(
                name: "ROW_VERSION",
                table: "Uom");

            migrationBuilder.DropColumn(
                name: "REC_CREATOR",
                table: "SystemSetting");

            migrationBuilder.DropColumn(
                name: "REC_CREDATE",
                table: "SystemSetting");

            migrationBuilder.DropColumn(
                name: "REC_DELETED",
                table: "SystemSetting");

            migrationBuilder.DropColumn(
                name: "REC_REVDATE",
                table: "SystemSetting");

            migrationBuilder.DropColumn(
                name: "REC_REVISOR",
                table: "SystemSetting");

            migrationBuilder.DropColumn(
                name: "ROW_VERSION",
                table: "SystemSetting");

            migrationBuilder.DropColumn(
                name: "REC_CREATOR",
                table: "StockLedgerEntry");

            migrationBuilder.DropColumn(
                name: "REC_CREDATE",
                table: "StockLedgerEntry");

            migrationBuilder.DropColumn(
                name: "REC_DELETED",
                table: "StockLedgerEntry");

            migrationBuilder.DropColumn(
                name: "REC_REVDATE",
                table: "StockLedgerEntry");

            migrationBuilder.DropColumn(
                name: "REC_REVISOR",
                table: "StockLedgerEntry");

            migrationBuilder.DropColumn(
                name: "ROW_VERSION",
                table: "StockLedgerEntry");

            migrationBuilder.DropColumn(
                name: "REC_CREATOR",
                table: "StockDocumentLine");

            migrationBuilder.DropColumn(
                name: "REC_CREDATE",
                table: "StockDocumentLine");

            migrationBuilder.DropColumn(
                name: "REC_DELETED",
                table: "StockDocumentLine");

            migrationBuilder.DropColumn(
                name: "REC_REVDATE",
                table: "StockDocumentLine");

            migrationBuilder.DropColumn(
                name: "REC_REVISOR",
                table: "StockDocumentLine");

            migrationBuilder.DropColumn(
                name: "ROW_VERSION",
                table: "StockDocumentLine");

            migrationBuilder.DropColumn(
                name: "REC_CREATOR",
                table: "StockDocument");

            migrationBuilder.DropColumn(
                name: "REC_CREDATE",
                table: "StockDocument");

            migrationBuilder.DropColumn(
                name: "REC_DELETED",
                table: "StockDocument");

            migrationBuilder.DropColumn(
                name: "REC_REVDATE",
                table: "StockDocument");

            migrationBuilder.DropColumn(
                name: "REC_REVISOR",
                table: "StockDocument");

            migrationBuilder.DropColumn(
                name: "REC_CREATOR",
                table: "StockBalance");

            migrationBuilder.DropColumn(
                name: "REC_CREDATE",
                table: "StockBalance");

            migrationBuilder.DropColumn(
                name: "REC_DELETED",
                table: "StockBalance");

            migrationBuilder.DropColumn(
                name: "REC_REVDATE",
                table: "StockBalance");

            migrationBuilder.DropColumn(
                name: "REC_REVISOR",
                table: "StockBalance");

            migrationBuilder.DropColumn(
                name: "REC_CREATOR",
                table: "RefreshToken");

            migrationBuilder.DropColumn(
                name: "REC_CREDATE",
                table: "RefreshToken");

            migrationBuilder.DropColumn(
                name: "REC_DELETED",
                table: "RefreshToken");

            migrationBuilder.DropColumn(
                name: "REC_REVDATE",
                table: "RefreshToken");

            migrationBuilder.DropColumn(
                name: "REC_REVISOR",
                table: "RefreshToken");

            migrationBuilder.DropColumn(
                name: "REC_CREATOR",
                table: "ProductBarcode");

            migrationBuilder.DropColumn(
                name: "REC_CREDATE",
                table: "ProductBarcode");

            migrationBuilder.DropColumn(
                name: "REC_DELETED",
                table: "ProductBarcode");

            migrationBuilder.DropColumn(
                name: "REC_REVDATE",
                table: "ProductBarcode");

            migrationBuilder.DropColumn(
                name: "REC_REVISOR",
                table: "ProductBarcode");

            migrationBuilder.DropColumn(
                name: "ROW_VERSION",
                table: "ProductBarcode");

            migrationBuilder.DropColumn(
                name: "REC_CREATOR",
                table: "Product");

            migrationBuilder.DropColumn(
                name: "REC_CREDATE",
                table: "Product");

            migrationBuilder.DropColumn(
                name: "REC_DELETED",
                table: "Product");

            migrationBuilder.DropColumn(
                name: "REC_REVDATE",
                table: "Product");

            migrationBuilder.DropColumn(
                name: "REC_REVISOR",
                table: "Product");

            migrationBuilder.DropColumn(
                name: "ROW_VERSION",
                table: "Product");

            migrationBuilder.DropColumn(
                name: "REC_CREATOR",
                table: "Partner");

            migrationBuilder.DropColumn(
                name: "REC_CREDATE",
                table: "Partner");

            migrationBuilder.DropColumn(
                name: "REC_DELETED",
                table: "Partner");

            migrationBuilder.DropColumn(
                name: "REC_REVDATE",
                table: "Partner");

            migrationBuilder.DropColumn(
                name: "REC_REVISOR",
                table: "Partner");

            migrationBuilder.DropColumn(
                name: "ROW_VERSION",
                table: "Partner");

            migrationBuilder.DropColumn(
                name: "REC_CREATOR",
                table: "DocumentSequence");

            migrationBuilder.DropColumn(
                name: "REC_CREDATE",
                table: "DocumentSequence");

            migrationBuilder.DropColumn(
                name: "REC_DELETED",
                table: "DocumentSequence");

            migrationBuilder.DropColumn(
                name: "REC_REVDATE",
                table: "DocumentSequence");

            migrationBuilder.DropColumn(
                name: "REC_REVISOR",
                table: "DocumentSequence");

            migrationBuilder.DropColumn(
                name: "REC_CREATOR",
                table: "Category");

            migrationBuilder.DropColumn(
                name: "REC_CREDATE",
                table: "Category");

            migrationBuilder.DropColumn(
                name: "REC_DELETED",
                table: "Category");

            migrationBuilder.DropColumn(
                name: "REC_REVDATE",
                table: "Category");

            migrationBuilder.DropColumn(
                name: "REC_REVISOR",
                table: "Category");

            migrationBuilder.DropColumn(
                name: "ROW_VERSION",
                table: "Category");

            migrationBuilder.DropColumn(
                name: "REC_CREATOR",
                table: "AuditLogs");

            migrationBuilder.DropColumn(
                name: "REC_CREDATE",
                table: "AuditLogs");

            migrationBuilder.DropColumn(
                name: "REC_DELETED",
                table: "AuditLogs");

            migrationBuilder.DropColumn(
                name: "REC_REVDATE",
                table: "AuditLogs");

            migrationBuilder.DropColumn(
                name: "REC_REVISOR",
                table: "AuditLogs");

            migrationBuilder.DropColumn(
                name: "ROW_VERSION",
                table: "AuditLogs");

            migrationBuilder.DropColumn(
                name: "REC_CREATOR",
                table: "Attachment");

            migrationBuilder.DropColumn(
                name: "REC_CREDATE",
                table: "Attachment");

            migrationBuilder.DropColumn(
                name: "REC_DELETED",
                table: "Attachment");

            migrationBuilder.DropColumn(
                name: "REC_REVISOR",
                table: "Attachment");

            migrationBuilder.DropColumn(
                name: "ROW_VERSION",
                table: "Attachment");

            migrationBuilder.DropColumn(
                name: "REC_CREATOR",
                table: "ApprovalRule");

            migrationBuilder.DropColumn(
                name: "REC_CREDATE",
                table: "ApprovalRule");

            migrationBuilder.DropColumn(
                name: "REC_DELETED",
                table: "ApprovalRule");

            migrationBuilder.DropColumn(
                name: "REC_REVDATE",
                table: "ApprovalRule");

            migrationBuilder.DropColumn(
                name: "REC_REVISOR",
                table: "ApprovalRule");

            migrationBuilder.DropColumn(
                name: "ROW_VERSION",
                table: "ApprovalRule");

            migrationBuilder.RenameColumn(
                name: "ROW_VERSION",
                table: "User",
                newName: "RowVersion");

            migrationBuilder.RenameColumn(
                name: "ROW_VERSION",
                table: "StockDocument",
                newName: "RowVersion");

            migrationBuilder.RenameColumn(
                name: "ROW_VERSION",
                table: "StockBalance",
                newName: "RowVersion");

            migrationBuilder.RenameColumn(
                name: "ROW_VERSION",
                table: "RefreshToken",
                newName: "RowVersion");

            migrationBuilder.RenameColumn(
                name: "ROW_VERSION",
                table: "DocumentSequence",
                newName: "RowVersion");

            migrationBuilder.RenameColumn(
                name: "REC_REVDATE",
                table: "Attachment",
                newName: "DeletedAt");

            migrationBuilder.AddPrimaryKey(
                name: "PK_UserWarehouse",
                table: "UserWarehouse",
                columns: new[] { "UserId", "WarehouseId" });

            migrationBuilder.CreateIndex(
                name: "IX_Warehouse_Code",
                table: "Warehouse",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_User_Email",
                table: "User",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_User_Username",
                table: "User",
                column: "Username",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UomConversion_ProductId_FromUomId_ToUomId",
                table: "UomConversion",
                columns: new[] { "ProductId", "FromUomId", "ToUomId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Uom_Code",
                table: "Uom",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StockDocument_No",
                table: "StockDocument",
                column: "No",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RefreshToken_TokenHash",
                table: "RefreshToken",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductBarcode_Barcode",
                table: "ProductBarcode",
                column: "Barcode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Product_Sku",
                table: "Product",
                column: "Sku",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Category_Name",
                table: "Category",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Attachment_OwnerType_OwnerId",
                table: "Attachment",
                columns: new[] { "OwnerType", "OwnerId" },
                filter: "[DeletedAt] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Attachment_StoragePath",
                table: "Attachment",
                column: "StoragePath",
                unique: true);
        }
    }
}
