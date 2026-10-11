using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ims.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class StoredLineAmount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "Amount",
                table: "StockDocumentLine",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "EnteredUnitCost",
                table: "StockDocumentLine",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            // Existing lines: the amount is what the API has been showing, round(base quantity x cost per base unit, 2) (SQL ROUND goes half away from zero, like
            // Money.Round), and the cost per entered unit is that cost spread back over the entered quantity (for a receipt in the base unit, the cost that was typed).
            // Both are clamped to the largest line amount (999,999,999,999,999) so that no absurd row can stop the migration with an overflow.
            migrationBuilder.Sql(@"
UPDATE [StockDocumentLine]
SET [Amount] = CASE WHEN ABS([BaseQty] * [UnitCost]) > 999999999999999 THEN 999999999999999 ELSE ROUND([BaseQty] * [UnitCost], 2) END,
    [EnteredUnitCost] = CASE WHEN [Qty] = 0 THEN 0
                             WHEN ABS([UnitCost] * [BaseQty] / [Qty]) > 999999999999999 THEN 999999999999999
                             ELSE ROUND([UnitCost] * [BaseQty] / [Qty], 2) END;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Amount",
                table: "StockDocumentLine");

            migrationBuilder.DropColumn(
                name: "EnteredUnitCost",
                table: "StockDocumentLine");
        }
    }
}
