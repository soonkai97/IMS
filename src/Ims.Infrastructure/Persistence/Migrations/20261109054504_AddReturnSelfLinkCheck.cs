using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ims.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// PL3-M8 (PL3.17 review): <c>CK_StockDocument_ReturnNotSelf</c>, a document is never its own return. The domain cannot produce it (a return is created with a new key), so this is only the database saying it; it is added WITH CHECK,
    /// which reads <c>StockDocument</c> once to validate (every row has a NULL link on a database that has no returns yet, and no row can be its own return on one that has). Down drops it, which is metadata-only.
    /// </summary>
    public partial class AddReturnSelfLinkCheck : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddCheckConstraint(
                name: "CK_StockDocument_ReturnNotSelf",
                table: "StockDocument",
                sql: "[ReturnOfDocumentId] IS NULL OR [ReturnOfDocumentId] <> [Id]");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_StockDocument_ReturnNotSelf",
                table: "StockDocument");
        }
    }
}
