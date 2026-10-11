using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ims.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AverageCostSnapshotAndCostScale : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Costs per base unit and the average keep 10 decimal places inside the system (Limits.CostDecimals) so a unit conversion or repeated
            // blending does not drift by a cent over a large quantity. Same storage size (precision 18); the integer part shrinks from 12 to 8 digits,
            // far above any unit cost. Nothing is rounded here: every existing value is kept as it is.
            migrationBuilder.AlterColumn<decimal>(
                name: "UnitCost",
                table: "StockLedgerEntry",
                type: "decimal(18,10)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,6)");

            migrationBuilder.AlterColumn<decimal>(
                name: "UnitCost",
                table: "StockDocumentLine",
                type: "decimal(18,10)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,6)");

            migrationBuilder.AddColumn<decimal>(
                name: "AvgCostAfter",
                table: "StockDocumentLine",
                type: "decimal(18,10)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "AvgCostBefore",
                table: "StockDocumentLine",
                type: "decimal(18,10)",
                nullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "AvgCost",
                table: "StockBalance",
                type: "decimal(18,10)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,6)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Back to 6 places: SQL Server rounds the values that have more.
            migrationBuilder.DropColumn(
                name: "AvgCostAfter",
                table: "StockDocumentLine");

            migrationBuilder.DropColumn(
                name: "AvgCostBefore",
                table: "StockDocumentLine");

            migrationBuilder.AlterColumn<decimal>(
                name: "UnitCost",
                table: "StockLedgerEntry",
                type: "decimal(18,6)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,10)");

            migrationBuilder.AlterColumn<decimal>(
                name: "UnitCost",
                table: "StockDocumentLine",
                type: "decimal(18,6)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,10)");

            migrationBuilder.AlterColumn<decimal>(
                name: "AvgCost",
                table: "StockBalance",
                type: "decimal(18,6)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,10)");
        }
    }
}
