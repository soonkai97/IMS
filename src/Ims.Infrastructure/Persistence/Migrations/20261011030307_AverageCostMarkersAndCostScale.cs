using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ims.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AverageCostMarkersAndCostScale : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // MAINTENANCE WINDOW. A cost per base unit and the moving average keep 10 decimal places (Limits.CostDecimals) in decimal(22,10): the integer part stays
            // 12 digits, as in the decimal(18,6) it replaces, so no value can overflow. Changing the scale rewrites the ledger, the document lines and the balances under a
            // schema-modification lock (the ledger is the big one; measured duration: docs/architecture/backend.md, "Costing and reversal"). Stop the API first.
            // No pre-check of the values is needed: nothing can overflow, because the old columns cannot hold a cost the new ones cannot.
            migrationBuilder.AlterColumn<decimal>(
                name: "UnitCost",
                table: "StockLedgerEntry",
                type: "decimal(22,10)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,6)");

            migrationBuilder.AlterColumn<decimal>(
                name: "UnitCost",
                table: "StockDocumentLine",
                type: "decimal(22,10)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,6)");

            migrationBuilder.AddColumn<decimal>(
                name: "AvgCostAfter",
                table: "StockDocumentLine",
                type: "decimal(22,10)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "AvgCostBefore",
                table: "StockDocumentLine",
                type: "decimal(22,10)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PreviousLineId",
                table: "StockDocumentLine",
                type: "int",
                nullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "AvgCost",
                table: "StockBalance",
                type: "decimal(22,10)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,6)");

            migrationBuilder.AddColumn<int>(
                name: "LastReceiptLineId",
                table: "StockBalance",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Back to decimal(18,6): the values keep 12 integer digits and SQL Server rounds the ones that have more than 6 decimals.
            migrationBuilder.DropColumn(
                name: "AvgCostAfter",
                table: "StockDocumentLine");

            migrationBuilder.DropColumn(
                name: "AvgCostBefore",
                table: "StockDocumentLine");

            migrationBuilder.DropColumn(
                name: "PreviousLineId",
                table: "StockDocumentLine");

            migrationBuilder.DropColumn(
                name: "LastReceiptLineId",
                table: "StockBalance");

            migrationBuilder.AlterColumn<decimal>(
                name: "UnitCost",
                table: "StockLedgerEntry",
                type: "decimal(18,6)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(22,10)");

            migrationBuilder.AlterColumn<decimal>(
                name: "UnitCost",
                table: "StockDocumentLine",
                type: "decimal(18,6)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(22,10)");

            migrationBuilder.AlterColumn<decimal>(
                name: "AvgCost",
                table: "StockBalance",
                type: "decimal(18,6)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(22,10)");
        }
    }
}
