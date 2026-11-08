using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ims.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// PL3-M4 (pilot slice PL3.5). The link between a delivery and the sales order it was created from, on the two BIG document tables: <c>StockDocument.SalesOrderId</c> (a foreign key to
    /// <c>SalesOrder</c>, restrict) and <c>StockDocumentLine.SalesOrderLineNo</c> (the order line's number; not a foreign key, an Approved order's lines never change). Both are nullable with NO
    /// default, so the add is metadata-only on SQL Server (the technique of <c>AddReceiptPurchaseOrderLinks</c>): no row is read or rewritten, and every existing row reads NULL, which the application treats as "not linked".
    /// One index on the document: <c>IX_StockDocument_SalesOrderId</c>, filtered on <c>IS NOT NULL</c>, so it holds only linked deliveries and costs nothing on a document that is not linked. The foreign
    /// key is added WITH CHECK over an all-NULL column; the timing at the 1M tier is recorded in <c>docs/dba/README.md</c>.
    /// The DBA deferrals of the PL3.2 review ride along because the sales tables are still empty: (1) a UNIQUE index on the live lines of an order, <c>(SalesOrderId, LineNo)</c>, because a delivery names
    /// a line by its number; (2) the foreign-key index of the order line now INCLUDEs <c>BaseQty</c> and <c>SellingAmount</c>, so the totals of the list and the detail read the index alone; (3) CHECK constraints
    /// <c>Qty &gt; 0</c>, <c>BaseQty &gt; 0</c> and <c>SellingPrice &gt;= 0</c> on the line (the domain already refuses the rest; the database now says it too).
    /// Down refuses while any document or line carries a link (live or soft-deleted rows both count), because dropping the columns would lose which order a delivery belongs to; with none it drops
    /// everything this added and puts the order-line index back as it was, so Up, Down, Up works on a database that has rows.
    /// </summary>
    public partial class AddDeliverySalesOrderLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SalesOrderId",
                table: "StockDocument",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SalesOrderLineNo",
                table: "StockDocumentLine",
                type: "int",
                nullable: true);

            // The index on the new column is created by dynamic SQL on purpose, with QUOTED_IDENTIFIER ON inside it (see AddReceiptPurchaseOrderLinks): in a script run by hand the ADD COLUMN and a plain
            // CREATE INDEX would be one batch, and sqlcmd has QUOTED_IDENTIFIER OFF, which a filtered index refuses (1934) while the rest of the script would go on and record the migration as applied.
            migrationBuilder.Sql("EXEC(N'SET QUOTED_IDENTIFIER ON; CREATE INDEX [IX_StockDocument_SalesOrderId] ON [StockDocument] ([SalesOrderId]) WHERE [SalesOrderId] IS NOT NULL');");

            migrationBuilder.AddForeignKey(
                name: "FK_StockDocument_SalesOrder_SalesOrderId",
                table: "StockDocument",
                column: "SalesOrderId",
                principalTable: "SalesOrder",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // The sales-order line (an empty table): the covering foreign-key index, the unique live line number and the checks.
            migrationBuilder.DropIndex(
                name: "IX_SalesOrderLine_SalesOrderId",
                table: "SalesOrderLine");

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrderLine_SalesOrderId",
                table: "SalesOrderLine",
                column: "SalesOrderId")
                .Annotation("SqlServer:Include", new[] { "BaseQty", "SellingAmount" });

            migrationBuilder.Sql("EXEC(N'SET QUOTED_IDENTIFIER ON; CREATE UNIQUE INDEX [IX_SalesOrderLine_SalesOrderId_LineNo] ON [SalesOrderLine] ([SalesOrderId], [LineNo]) WHERE [REC_DELETED] = 0');");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SalesOrderLine_BaseQty",
                table: "SalesOrderLine",
                sql: "[BaseQty] > 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SalesOrderLine_Qty",
                table: "SalesOrderLine",
                sql: "[Qty] > 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SalesOrderLine_SellingPrice",
                table: "SalesOrderLine",
                sql: "[SellingPrice] >= 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The check and the drops of the link are ONE batch, on purpose (see AddTaxReadyFields): a script run by hand goes on to the next batch after an error, so a refusal in a batch of its own would be
            // followed by the drops. THROW ends this batch, so nothing is dropped. Soft-deleted rows count: the link is still stored on them.
            migrationBuilder.Sql("""
                SET XACT_ABORT ON; -- a run by hand that is stopped below leaves no open transaction
                IF EXISTS (SELECT 1 FROM [StockDocument] WHERE [SalesOrderId] IS NOT NULL)
                   OR EXISTS (SELECT 1 FROM [StockDocumentLine] WHERE [SalesOrderLineNo] IS NOT NULL)
                    THROW 50008, N'Deliveries are linked to sales orders (StockDocument.SalesOrderId or StockDocumentLine.SalesOrderLineNo). Dropping the columns would lose which order each delivery belongs to. Restore the backup, or clear the links, then run the migration again.', 1;
                ALTER TABLE [StockDocument] DROP CONSTRAINT [FK_StockDocument_SalesOrder_SalesOrderId];
                DROP INDEX [IX_StockDocument_SalesOrderId] ON [StockDocument];
                ALTER TABLE [StockDocument] DROP COLUMN [SalesOrderId];
                ALTER TABLE [StockDocumentLine] DROP COLUMN [SalesOrderLineNo];
                """);

            migrationBuilder.DropCheckConstraint(
                name: "CK_SalesOrderLine_BaseQty",
                table: "SalesOrderLine");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SalesOrderLine_Qty",
                table: "SalesOrderLine");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SalesOrderLine_SellingPrice",
                table: "SalesOrderLine");

            migrationBuilder.DropIndex(
                name: "IX_SalesOrderLine_SalesOrderId_LineNo",
                table: "SalesOrderLine");

            migrationBuilder.DropIndex(
                name: "IX_SalesOrderLine_SalesOrderId",
                table: "SalesOrderLine");

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrderLine_SalesOrderId",
                table: "SalesOrderLine",
                column: "SalesOrderId");
        }
    }
}
