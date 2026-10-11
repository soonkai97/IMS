using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ims.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentReversedBy : Migration
    {
        /// <summary><c>(int)DocumentStatus.Reversed</c> as stored.</summary>
        public const int ReversedStatus = 4;
        /// <summary><c>(int)LedgerReason.Reversal</c> as stored.</summary>
        public const int ReversalReason = 6;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ReversedBy",
                table: "StockDocument",
                type: "int",
                nullable: true);

            // Documents reversed before this column existed: the user is the creator of their reversal ledger entries, which are immutable. Two gaps stay empty on
            // purpose and the document then shows "Reversed" without a name: a reversed document that has no reversal entry in the ledger (nothing to learn the user
            // from), and a document reversed by the OLD code while a rolling deploy runs (it does not write the column). Neither is worth a guess. The two numbers are the stored values of the enums (a migration must not follow later renumbering of the enums, so it
            // states them; MigrationEnumValuesTests fails if the enums ever change under it).
            migrationBuilder.Sql($@"
UPDATE d
SET d.[ReversedBy] = (SELECT TOP 1 l.[CreatedBy] FROM [StockLedgerEntry] l WHERE l.[SourceDocId] = d.[Id] AND l.[Reason] = {ReversalReason} ORDER BY l.[Id])
FROM [StockDocument] d
WHERE d.[Status] = {ReversedStatus} AND d.[ReversedBy] IS NULL;");

            migrationBuilder.CreateIndex(
                name: "IX_StockDocument_ReversedBy",
                table: "StockDocument",
                column: "ReversedBy",
                filter: "[ReversedBy] IS NOT NULL");

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

            migrationBuilder.DropColumn(
                name: "ReversedBy",
                table: "StockDocument");
        }
    }
}
