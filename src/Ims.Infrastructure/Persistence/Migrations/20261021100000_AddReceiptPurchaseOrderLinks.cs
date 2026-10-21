using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ims.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// The link between a receipt and the purchase order it was created from (pilot slice PL2.4), on the two BIG document tables: <c>StockDocument.PurchaseOrderId</c> (a foreign key to
    /// <c>PurchaseOrder</c>, restrict) and <c>StockDocumentLine.PurchaseOrderLineNo</c> (the order line's number; not a foreign key, an Approved order's lines never change). Both are nullable
    /// with NO default, so the add is metadata-only on SQL Server: no row is read or rewritten, whatever the table size (the same technique as <c>AddTaxReadyFields</c>), and every existing row
    /// reads NULL, which the application treats as "not linked". NOT NULL is never added.
    /// One index, on the document: <c>IX_StockDocument_PurchaseOrderId</c>, filtered on <c>IS NOT NULL</c>, so it holds only linked receipts (none on an existing database) and costs nothing on a
    /// document that is not linked. It serves "the receipts of this order" (the order's detail and the cancel check) and the start of the received-quantity sum (documents of the order, then
    /// their lines through the existing <c>IX_StockDocumentLine_DocumentId</c>). The line gets no index: the sum reaches the lines through their document. The foreign key is added WITH CHECK
    /// over an all-NULL column, which validates by reading the table once; the timing at the 1M tier is recorded in <c>docs/dba/README.md</c>.
    /// Down refuses while any document or line carries a link (live or soft-deleted rows both count), because dropping the columns would lose which order a receipt belongs to; with none it drops the
    /// foreign key, the index and the columns, so Up, Down, Up works on a database that has rows.
    /// </summary>
    public partial class AddReceiptPurchaseOrderLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PurchaseOrderId",
                table: "StockDocument",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PurchaseOrderLineNo",
                table: "StockDocumentLine",
                type: "int",
                nullable: true);

            // The index is created by dynamic SQL on purpose. In the script a DBA runs by hand, the two ADD COLUMN statements and a plain CREATE INDEX on the new column would be ONE batch, and
            // SQL Server resolves the column names of the index when it compiles the batch, before the column exists ("Invalid column name"). EXEC is compiled later; the statement that follows
            // (the foreign key) starts a new batch, because EF ends a raw SQL operation with GO. The runner never had the problem (it sends each operation alone); the generated script is tested.
            // A filtered index also needs QUOTED_IDENTIFIER ON, which sqlcmd has OFF by default: the SET inside the EXEC makes the script independent of the client setting (without it the statement fails
            // with 1934 while the rest of the script, the history row included, would go on and record the migration as applied WITHOUT the index).
            migrationBuilder.Sql("EXEC(N'SET QUOTED_IDENTIFIER ON; CREATE INDEX [IX_StockDocument_PurchaseOrderId] ON [StockDocument] ([PurchaseOrderId]) WHERE [PurchaseOrderId] IS NOT NULL');");

            migrationBuilder.AddForeignKey(
                name: "FK_StockDocument_PurchaseOrder_PurchaseOrderId",
                table: "StockDocument",
                column: "PurchaseOrderId",
                principalTable: "PurchaseOrder",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The check and the drops are ONE batch, on purpose (see AddTaxReadyFields): a script run by hand goes on to the next batch after an error, so a refusal in a batch of its own
            // would be followed by the drops. THROW ends this batch, so nothing is dropped. Soft-deleted rows count: the link is still stored on them.
            migrationBuilder.Sql("""
                SET XACT_ABORT ON; -- a run by hand that is stopped below leaves no open transaction
                IF EXISTS (SELECT 1 FROM [StockDocument] WHERE [PurchaseOrderId] IS NOT NULL)
                   OR EXISTS (SELECT 1 FROM [StockDocumentLine] WHERE [PurchaseOrderLineNo] IS NOT NULL)
                    THROW 50006, N'Receipts are linked to purchase orders (StockDocument.PurchaseOrderId or StockDocumentLine.PurchaseOrderLineNo). Dropping the columns would lose which order each receipt belongs to. Restore the backup, or clear the links, then run the migration again.', 1;
                ALTER TABLE [StockDocument] DROP CONSTRAINT [FK_StockDocument_PurchaseOrder_PurchaseOrderId];
                DROP INDEX [IX_StockDocument_PurchaseOrderId] ON [StockDocument];
                ALTER TABLE [StockDocument] DROP COLUMN [PurchaseOrderId];
                ALTER TABLE [StockDocumentLine] DROP COLUMN [PurchaseOrderLineNo];
                """);
        }
    }
}
