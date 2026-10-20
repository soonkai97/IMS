using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ims.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Purchase orders (pilot slice PL2.2): two NEW tables, <c>PurchaseOrder</c> and <c>PurchaseOrderLine</c>, empty. No existing table or row is touched, so Up takes metadata time only, whatever
    /// the database size. The workflow columns (submitter, approver, closer, reasons, close kind) are in from the start so that the workflow slice needs no migration of its own.
    /// Indexes: the number (unique among live rows), the list by status newest first (also the count of open orders that blocks switching the module off), and the foreign keys; the
    /// nullable user columns have filtered foreign-key indexes. The line index on the product covers the on-order sum of the reorder list. There is no index on the creation date: an order list
    /// is thousands of rows a year, not millions. Down refuses while any order exists (live or soft-deleted), because dropping the tables would lose them silently; with none it drops both,
    /// so Up, Down, Up works on a database that has other rows.
    /// </summary>
    public partial class AddPurchaseOrders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PurchaseOrder",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    No = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    WarehouseId = table.Column<int>(type: "int", nullable: false),
                    SupplierId = table.Column<int>(type: "int", nullable: false),
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
                    table.PrimaryKey("PK_PurchaseOrder", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PurchaseOrder_Partner_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "Partner",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseOrder_User_ApprovedBy",
                        column: x => x.ApprovedBy,
                        principalTable: "User",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseOrder_User_ClosedBy",
                        column: x => x.ClosedBy,
                        principalTable: "User",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseOrder_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseOrder_User_RejectedBy",
                        column: x => x.RejectedBy,
                        principalTable: "User",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseOrder_User_SubmittedBy",
                        column: x => x.SubmittedBy,
                        principalTable: "User",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseOrder_Warehouse_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouse",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PurchaseOrderLine",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PurchaseOrderId = table.Column<int>(type: "int", nullable: false),
                    LineNo = table.Column<int>(type: "int", nullable: false),
                    ProductId = table.Column<int>(type: "int", nullable: false),
                    Qty = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    UomId = table.Column<int>(type: "int", nullable: false),
                    BaseQty = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    UnitPrice = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
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
                    table.PrimaryKey("PK_PurchaseOrderLine", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PurchaseOrderLine_Product_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Product",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseOrderLine_PurchaseOrder_PurchaseOrderId",
                        column: x => x.PurchaseOrderId,
                        principalTable: "PurchaseOrder",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PurchaseOrderLine_Uom_UomId",
                        column: x => x.UomId,
                        principalTable: "Uom",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrder_ApprovedBy",
                table: "PurchaseOrder",
                column: "ApprovedBy",
                filter: "[ApprovedBy] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrder_ClosedBy",
                table: "PurchaseOrder",
                column: "ClosedBy",
                filter: "[ClosedBy] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrder_CreatedBy",
                table: "PurchaseOrder",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrder_No",
                table: "PurchaseOrder",
                column: "No",
                unique: true,
                filter: "[REC_DELETED] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrder_RejectedBy",
                table: "PurchaseOrder",
                column: "RejectedBy",
                filter: "[RejectedBy] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrder_Status_Id",
                table: "PurchaseOrder",
                columns: new[] { "Status", "Id" },
                descending: new[] { false, true },
                filter: "[REC_DELETED] = 0")
                .Annotation("SqlServer:Include", new[] { "WarehouseId" });

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrder_SubmittedBy",
                table: "PurchaseOrder",
                column: "SubmittedBy",
                filter: "[SubmittedBy] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrder_SupplierId",
                table: "PurchaseOrder",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrder_WarehouseId",
                table: "PurchaseOrder",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "UX_PurchaseOrder_PublicId",
                table: "PurchaseOrder",
                column: "PublicId",
                unique: true)
                .Annotation("SqlServer:FillFactor", 90);

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrderLine_ProductId",
                table: "PurchaseOrderLine",
                column: "ProductId")
                .Annotation("SqlServer:Include", new[] { "PurchaseOrderId", "BaseQty" });

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrderLine_PurchaseOrderId",
                table: "PurchaseOrderLine",
                column: "PurchaseOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrderLine_UomId",
                table: "PurchaseOrderLine",
                column: "UomId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The check and the drops are ONE batch, on purpose (see AddTaxReadyFields): a script run by hand goes on to the next batch after an error, so a refusal in a batch of its own
            // would be followed by the drops. THROW ends this batch, so neither table is dropped. Soft-deleted orders count: the rows are still there.
            migrationBuilder.Sql("""
                SET XACT_ABORT ON; -- a run by hand that is stopped below leaves no open transaction
                IF EXISTS (SELECT 1 FROM [PurchaseOrder])
                    THROW 50005, N'Purchase orders exist. Dropping the PurchaseOrder and PurchaseOrderLine tables would lose them. Restore the backup, or delete the orders first, then run the migration again.', 1;
                DROP TABLE [PurchaseOrderLine];
                DROP TABLE [PurchaseOrder];
                """);
        }
    }
}
