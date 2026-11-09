using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ims.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// PL3-M7 (pilot slice PL3.16). The link between a return and the document it returns, on the two BIG document tables: <c>StockDocument.ReturnOfDocumentId</c> (a foreign key to <c>StockDocument</c> itself,
    /// restrict) and <c>StockDocumentLine.ReturnOfLineNo</c> (the position of the original line, 1 to n over the original's lines in key order; not a foreign key and not a row id: a Posted document's lines never
    /// change). Both are nullable with NO default, so the add is metadata-only on SQL Server (the technique of <c>AddDeliverySalesOrderLinks</c>): no row is read or rewritten, and every existing row reads NULL,
    /// which the application treats as "not a return". One index on the document: <c>IX_StockDocument_ReturnOfDocumentId</c>, filtered on <c>IS NOT NULL</c>, so it holds only returns and costs nothing on a document
    /// that is not one. The foreign key is added WITH CHECK over an all-NULL column.
    /// <c>CK_StockDocument_OneLink</c> is replaced by the same rule over three links (a purchase order, a sales order, a document returned): at most one is set. It is dropped and added again WITH CHECK, which reads the
    /// table once to validate; the timing at the 1M tier, or the reason it was not taken, is recorded in <c>docs/dba/README.md</c> (section 16).
    /// Down refuses while any document or line carries a return link (live or soft-deleted rows both count), because dropping the columns would lose which document each return belongs to; with none it drops
    /// everything this added and puts the two-link rule back, so Up, Down, Up works on a database that has rows.
    /// </summary>
    public partial class AddReturnLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ReturnOfDocumentId",
                table: "StockDocument",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReturnOfLineNo",
                table: "StockDocumentLine",
                type: "int",
                nullable: true);

            // By dynamic SQL, with QUOTED_IDENTIFIER ON inside it (see AddDeliverySalesOrderLinks): in a script run by hand the ADD COLUMN and a plain CREATE INDEX would be one batch, and sqlcmd has
            // QUOTED_IDENTIFIER OFF, which a filtered index refuses (1934) while the rest of the script would go on and record the migration as applied.
            migrationBuilder.Sql("EXEC(N'SET QUOTED_IDENTIFIER ON; CREATE INDEX [IX_StockDocument_ReturnOfDocumentId] ON [StockDocument] ([ReturnOfDocumentId]) WHERE [ReturnOfDocumentId] IS NOT NULL');");

            migrationBuilder.AddForeignKey(
                name: "FK_StockDocument_StockDocument_ReturnOfDocumentId",
                table: "StockDocument",
                column: "ReturnOfDocumentId",
                principalTable: "StockDocument",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // At most one of three links. The old rule is dropped and the new one added WITH CHECK (dynamic SQL, because the rule names the new column and a script run by hand would otherwise compile it
            // in the batch that adds the column).
            migrationBuilder.DropCheckConstraint(
                name: "CK_StockDocument_OneLink",
                table: "StockDocument");

            migrationBuilder.Sql("EXEC(N'ALTER TABLE [StockDocument] WITH CHECK ADD CONSTRAINT [CK_StockDocument_OneLink] CHECK ((CASE WHEN [PurchaseOrderId] IS NULL THEN 0 ELSE 1 END + CASE WHEN [SalesOrderId] IS NULL THEN 0 ELSE 1 END + CASE WHEN [ReturnOfDocumentId] IS NULL THEN 0 ELSE 1 END) <= 1)');");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The check and the drops are ONE batch, on purpose (see AddDeliverySalesOrderLinks): a script run by hand goes on to the next batch after an error, so a refusal in a batch of its own would be
            // followed by the drops. THROW ends this batch, so nothing is dropped. Soft-deleted rows count: the link is still stored on them.
            migrationBuilder.Sql("""
                SET XACT_ABORT ON; -- a run by hand that is stopped below leaves no open transaction
                IF EXISTS (SELECT 1 FROM [StockDocument] WHERE [ReturnOfDocumentId] IS NOT NULL)
                   OR EXISTS (SELECT 1 FROM [StockDocumentLine] WHERE [ReturnOfLineNo] IS NOT NULL)
                    THROW 50011, N'Returns are linked to the documents they return (StockDocument.ReturnOfDocumentId or StockDocumentLine.ReturnOfLineNo). Dropping the columns would lose which document each return belongs to. Restore the backup, or clear the links, then run the migration again.', 1;
                ALTER TABLE [StockDocument] DROP CONSTRAINT [CK_StockDocument_OneLink];
                ALTER TABLE [StockDocument] DROP CONSTRAINT [FK_StockDocument_StockDocument_ReturnOfDocumentId];
                DROP INDEX [IX_StockDocument_ReturnOfDocumentId] ON [StockDocument];
                ALTER TABLE [StockDocument] DROP COLUMN [ReturnOfDocumentId];
                ALTER TABLE [StockDocumentLine] DROP COLUMN [ReturnOfLineNo];
                ALTER TABLE [StockDocument] WITH CHECK ADD CONSTRAINT [CK_StockDocument_OneLink] CHECK ([PurchaseOrderId] IS NULL OR [SalesOrderId] IS NULL);
                """);
        }
    }
}
