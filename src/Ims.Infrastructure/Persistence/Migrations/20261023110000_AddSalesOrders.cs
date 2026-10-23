using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ims.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// PL3-M2 (pilot slice PL3.2): the sales order tables, empty. Their own tables, never a StockDocument type (plan D1). The workflow columns (submitter, approver, closer, reasons, close kind) are in
    /// from the start so the workflow slice (PL3.3) needs no migration. Indexes: the number (unique among live rows), the list by status newest first (also the count of open orders that blocks
    /// switching the module off), the customer, and the filtered foreign-key indexes of the nullable user columns; the line index on the product covers the delivered sums of later slices. Down refuses
    /// while any order exists (live or soft-deleted), because dropping the tables would lose them silently.
    /// </summary>
    public partial class AddSalesOrders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SalesOrder",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    No = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    WarehouseId = table.Column<int>(type: "int", nullable: false),
                    CustomerId = table.Column<int>(type: "int", nullable: false),
                    Remark = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedBy = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SubmittedBy = table.Column<int>(type: "int", nullable: true),
                    SubmittedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ApprovedBy = table.Column<int>(type: "int", nullable: true),
                    ApprovedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ClosedBy = table.Column<int>(type: "int", nullable: true),
                    ClosedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RequiresApproval = table.Column<bool>(type: "bit", nullable: false),
                    IsLarge = table.Column<bool>(type: "bit", nullable: false),
                    RejectionReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RejectedBy = table.Column<int>(type: "int", nullable: true),
                    RejectedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CancelReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CloseKind = table.Column<int>(type: "int", nullable: true),
                    CloseReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    REC_DELETED = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    REC_CREATOR = table.Column<int>(type: "int", nullable: true),
                    REC_CREDATE = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    REC_REVISOR = table.Column<int>(type: "int", nullable: true),
                    REC_REVDATE = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ROW_VERSION = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesOrder", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SalesOrder_Partner_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Partner",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SalesOrder_User_ApprovedBy",
                        column: x => x.ApprovedBy,
                        principalTable: "User",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SalesOrder_User_ClosedBy",
                        column: x => x.ClosedBy,
                        principalTable: "User",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SalesOrder_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SalesOrder_User_RejectedBy",
                        column: x => x.RejectedBy,
                        principalTable: "User",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SalesOrder_User_SubmittedBy",
                        column: x => x.SubmittedBy,
                        principalTable: "User",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SalesOrder_Warehouse_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouse",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SalesOrderLine",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SalesOrderId = table.Column<int>(type: "int", nullable: false),
                    LineNo = table.Column<int>(type: "int", nullable: false),
                    ProductId = table.Column<int>(type: "int", nullable: false),
                    Qty = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    UomId = table.Column<int>(type: "int", nullable: false),
                    BaseQty = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    SellingPrice = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    SellingAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Remark = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    REC_DELETED = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    REC_CREATOR = table.Column<int>(type: "int", nullable: true),
                    REC_CREDATE = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    REC_REVISOR = table.Column<int>(type: "int", nullable: true),
                    REC_REVDATE = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ROW_VERSION = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesOrderLine", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SalesOrderLine_Product_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Product",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SalesOrderLine_SalesOrder_SalesOrderId",
                        column: x => x.SalesOrderId,
                        principalTable: "SalesOrder",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SalesOrderLine_Uom_UomId",
                        column: x => x.UomId,
                        principalTable: "Uom",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrder_ApprovedBy",
                table: "SalesOrder",
                column: "ApprovedBy",
                filter: "[ApprovedBy] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrder_ClosedBy",
                table: "SalesOrder",
                column: "ClosedBy",
                filter: "[ClosedBy] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrder_CreatedBy",
                table: "SalesOrder",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrder_CustomerId",
                table: "SalesOrder",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrder_No",
                table: "SalesOrder",
                column: "No",
                unique: true,
                filter: "[REC_DELETED] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrder_RejectedBy",
                table: "SalesOrder",
                column: "RejectedBy",
                filter: "[RejectedBy] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrder_Status_Id",
                table: "SalesOrder",
                columns: new[] { "Status", "Id" },
                descending: new[] { false, true },
                filter: "[REC_DELETED] = 0")
                .Annotation("SqlServer:Include", new[] { "WarehouseId" });

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrder_SubmittedBy",
                table: "SalesOrder",
                column: "SubmittedBy",
                filter: "[SubmittedBy] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrder_WarehouseId",
                table: "SalesOrder",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "UX_SalesOrder_PublicId",
                table: "SalesOrder",
                column: "PublicId",
                unique: true)
                .Annotation("SqlServer:FillFactor", 90);

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrderLine_ProductId",
                table: "SalesOrderLine",
                column: "ProductId")
                .Annotation("SqlServer:Include", new[] { "SalesOrderId", "BaseQty" });

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrderLine_SalesOrderId",
                table: "SalesOrderLine",
                column: "SalesOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrderLine_UomId",
                table: "SalesOrderLine",
                column: "UomId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The check and the drops are ONE batch, on purpose (see AddPurchaseOrders): THROW ends the batch, so neither table is dropped. Soft-deleted orders count: the rows are still there.
            migrationBuilder.Sql("""
                SET XACT_ABORT ON; -- a run by hand that is stopped below leaves no open transaction
                IF EXISTS (SELECT 1 FROM [SalesOrder])
                    THROW 50007, N'Sales orders exist. Dropping the SalesOrder and SalesOrderLine tables would lose them. Restore the backup, or delete the orders first, then run the migration again.', 1;
                DROP TABLE [SalesOrderLine];
                DROP TABLE [SalesOrder];
                """);
        }
    }
}
