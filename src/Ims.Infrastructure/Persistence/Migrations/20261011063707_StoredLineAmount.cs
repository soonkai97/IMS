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

            migrationBuilder.AddColumn<bool>(
                name: "CostFromAverage",
                table: "StockDocumentLine",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "EnteredUnitCost",
                table: "StockDocumentLine",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            // Existing lines (SQL ROUND goes half away from zero, like Money.Round; every figure is clamped to the largest line amount so that no absurd row can stop the
            // migration with an overflow):
            //  * EnteredUnitCost is the stored cost per base unit spread back over the entered quantity, rounded to 2 decimals.
            //  * CostFromAverage is set for every issue line, and for an OPEN (Draft, Submitted, Approved) receipt line whose cost per entered unit has a fraction of a cent
            //    (typed before the 2-decimal rule, or an average): such a line cannot be shown as a typed 2-decimal cost, so it stays at its exact price when the draft is saved.
            //  * Amount: an open line with a typed cost gets round(entered quantity x entered unit cost, 2), the figure the new code computes, so saving it back changes
            //    nothing; every other line (posted, reversed, cancelled, at the average) keeps round(base quantity x cost per base unit, 2), what the API has been
            //    showing, so no posted total moves.
            migrationBuilder.Sql(@"
UPDATE l
SET [CostFromAverage] = x.[AtAverage],
    [EnteredUnitCost] = x.[Entered],
    [Amount] = CASE
        WHEN d.[Status] IN (0, 1, 2) AND x.[AtAverage] = 0
            THEN CASE WHEN ABS(l.[Qty] * x.[Entered]) > 999999999999999 THEN 999999999999999 ELSE ROUND(l.[Qty] * x.[Entered], 2) END
        ELSE CASE WHEN ABS(l.[BaseQty] * l.[UnitCost]) > 999999999999999 THEN 999999999999999 ELSE ROUND(l.[BaseQty] * l.[UnitCost], 2) END END
FROM [StockDocumentLine] l
JOIN [StockDocument] d ON d.[Id] = l.[DocumentId]
CROSS APPLY (SELECT CASE WHEN l.[Qty] = 0 THEN CAST(0 AS decimal(38, 10)) ELSE CAST(l.[UnitCost] * l.[BaseQty] / l.[Qty] AS decimal(38, 10)) END AS [Raw]) r
CROSS APPLY (SELECT
        CASE WHEN ABS(r.[Raw]) > 999999999999999 THEN CAST(999999999999999 AS decimal(38, 10)) ELSE ROUND(r.[Raw], 2) END AS [Entered],
        CASE WHEN d.[Type] = 2 THEN 1
             WHEN d.[Type] = 1 AND d.[Status] IN (0, 1, 2) AND ABS(r.[Raw] - ROUND(r.[Raw], 2)) > 0.0005 THEN 1
             ELSE 0 END AS [AtAverage]) x;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Amount",
                table: "StockDocumentLine");

            migrationBuilder.DropColumn(
                name: "CostFromAverage",
                table: "StockDocumentLine");

            migrationBuilder.DropColumn(
                name: "EnteredUnitCost",
                table: "StockDocumentLine");
        }
    }
}
