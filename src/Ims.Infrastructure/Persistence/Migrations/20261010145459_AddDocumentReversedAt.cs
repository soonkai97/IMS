using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ims.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentReversedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ReversedAt",
                table: "StockDocument",
                type: "datetime2",
                nullable: true);

            // Documents reversed before this column existed: the reversal time is the time of their reversal ledger entries (LedgerReason.Reversal = 6).
            // The movements report dates the reversal row with it, so a reversed document must never be left without one.
            migrationBuilder.Sql(@"
UPDATE d
SET d.[ReversedAt] = COALESCE(
    (SELECT MIN(l.[OccurredAt]) FROM [StockLedgerEntry] l WHERE l.[SourceDocId] = d.[Id] AND l.[Reason] = 6),
    d.[REC_REVDATE], d.[PostedAt])
FROM [StockDocument] d
WHERE d.[Status] = 4 AND d.[ReversedAt] IS NULL;");

            migrationBuilder.CreateIndex(
                name: "IX_StockDocument_ReversedAt",
                table: "StockDocument",
                column: "ReversedAt",
                filter: "[REC_DELETED] = 0 AND [ReversedAt] IS NOT NULL")
                .Annotation("SqlServer:Include", new[] { "Status", "Type", "WarehouseId", "PartnerId", "No" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_StockDocument_ReversedAt",
                table: "StockDocument");

            migrationBuilder.DropColumn(
                name: "ReversedAt",
                table: "StockDocument");
        }
    }
}
