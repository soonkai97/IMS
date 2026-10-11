using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ims.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DocumentSearchIndexAndReversedBy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ReversedBy",
                table: "StockDocument",
                type: "int",
                nullable: true);

            // Documents reversed before this column existed: the user is the creator of their reversal ledger entries (LedgerReason.Reversal = 6), which are
            // immutable. A reversed document must never show "Reversed by" empty.
            migrationBuilder.Sql(@"
UPDATE d
SET d.[ReversedBy] = (SELECT TOP 1 l.[CreatedBy] FROM [StockLedgerEntry] l WHERE l.[SourceDocId] = d.[Id] AND l.[Reason] = 6 ORDER BY l.[Id])
FROM [StockDocument] d
WHERE d.[Status] = 4 AND d.[ReversedBy] IS NULL;");

            migrationBuilder.CreateIndex(
                name: "IX_StockDocument_ReversedBy",
                table: "StockDocument",
                column: "ReversedBy");

            migrationBuilder.CreateIndex(
                name: "IX_StockDocument_Search",
                table: "StockDocument",
                column: "No",
                filter: "[REC_DELETED] = 0")
                .Annotation("SqlServer:Include", new[] { "PartnerId", "Remark" });

            migrationBuilder.AddForeignKey(
                name: "FK_StockDocument_User_ReversedBy",
                table: "StockDocument",
                column: "ReversedBy",
                principalTable: "User",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_StockDocument_User_ReversedBy",
                table: "StockDocument");

            migrationBuilder.DropIndex(
                name: "IX_StockDocument_ReversedBy",
                table: "StockDocument");

            migrationBuilder.DropIndex(
                name: "IX_StockDocument_Search",
                table: "StockDocument");

            migrationBuilder.DropColumn(
                name: "ReversedBy",
                table: "StockDocument");
        }
    }
}
