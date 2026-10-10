using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ims.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMovementsReportIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_StockDocumentLine_DocumentId_Live",
                table: "StockDocumentLine",
                column: "DocumentId",
                filter: "[REC_DELETED] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_StockDocument_PostedAt_Movements",
                table: "StockDocument",
                column: "PostedAt",
                filter: "[REC_DELETED] = 0 AND [PostedAt] IS NOT NULL")
                .Annotation("SqlServer:Include", new[] { "Status", "Type", "WarehouseId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_StockDocumentLine_DocumentId_Live",
                table: "StockDocumentLine");

            migrationBuilder.DropIndex(
                name: "IX_StockDocument_PostedAt_Movements",
                table: "StockDocument");
        }
    }
}
