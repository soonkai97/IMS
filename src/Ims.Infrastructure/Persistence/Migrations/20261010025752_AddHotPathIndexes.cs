using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ims.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddHotPathIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_StockDocument_Type_Status_WarehouseId",
                table: "StockDocument");

            migrationBuilder.CreateIndex(
                name: "IX_StockDocument_CreatedAt",
                table: "StockDocument",
                column: "CreatedAt",
                filter: "[REC_DELETED] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_StockDocument_Status_Type_Id",
                table: "StockDocument",
                columns: new[] { "Status", "Type", "Id" },
                descending: new[] { false, false, true },
                filter: "[REC_DELETED] = 0")
                .Annotation("SqlServer:Include", new[] { "WarehouseId" });

            migrationBuilder.CreateIndex(
                name: "IX_Product_Name",
                table: "Product",
                column: "Name",
                filter: "[REC_DELETED] = 0")
                .Annotation("SqlServer:Include", new[] { "Sku", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_Actor_Id",
                table: "AuditLogs",
                columns: new[] { "Actor", "Id" },
                descending: new[] { false, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_StockDocument_CreatedAt",
                table: "StockDocument");

            migrationBuilder.DropIndex(
                name: "IX_StockDocument_Status_Type_Id",
                table: "StockDocument");

            migrationBuilder.DropIndex(
                name: "IX_Product_Name",
                table: "Product");

            migrationBuilder.DropIndex(
                name: "IX_AuditLogs_Actor_Id",
                table: "AuditLogs");

            migrationBuilder.CreateIndex(
                name: "IX_StockDocument_Type_Status_WarehouseId",
                table: "StockDocument",
                columns: new[] { "Type", "Status", "WarehouseId" });
        }
    }
}
