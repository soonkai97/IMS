using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ims.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLedgerBalanceAfter : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "BalanceAfter",
                table: "StockLedgerEntry",
                type: "decimal(18,4)",
                nullable: false,
                defaultValue: 0m);

            // Rows written before the column existed: the balance after a row is the running sum of the product's movements in that warehouse up to and including
            // it, in ledger order (the ledger is written under the (warehouse, product) lock, so Id order is posting order). This is the one place where the
            // append-only ledger is updated; the trigger that forbids it is switched off for this statement only, inside the migration's transaction. Takes a
            // schema lock on the table: run it in the maintenance window (timings in docs/architecture/data-and-performance-rules.md section 9) and rebuild the
            // ledger's indexes afterwards (docs/dba/07-maintenance.sql), because the rows grew.
            migrationBuilder.Sql("DISABLE TRIGGER [TR_StockLedgerEntry_AppendOnly] ON [StockLedgerEntry];");
            migrationBuilder.Sql(@"
WITH running AS (
    SELECT [Id], SUM([QtyDelta]) OVER (PARTITION BY [ProductId], [WarehouseId] ORDER BY [Id] ROWS UNBOUNDED PRECEDING) AS [Balance]
    FROM [StockLedgerEntry])
UPDATE l SET l.[BalanceAfter] = r.[Balance]
FROM [StockLedgerEntry] l JOIN running r ON r.[Id] = l.[Id];");
            migrationBuilder.Sql("ENABLE TRIGGER [TR_StockLedgerEntry_AppendOnly] ON [StockLedgerEntry];");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BalanceAfter",
                table: "StockLedgerEntry");
        }
    }
}
