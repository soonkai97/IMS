using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ims.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Tax-ready fields of the accounting module (pilot slice PL1.5): <c>Product.HsCode</c>, <c>Product.SstCode</c>, <c>Partner.Tin</c> and <c>Partner.SstRegNo</c>. All four are
    /// nullable with no default and no index, so the add is metadata-only on SQL Server: no row is read or rewritten, whatever the table size (a nullable column without a default
    /// is recorded in the catalog only). Existing rows read NULL, which the application treats as "no value". The sizes and the allowed characters are the shape rules of
    /// <c>TaxFieldShapes</c>; the database keeps only the length (no CHECK constraint: the shape may be tightened or loosened by a release without a migration).
    /// Down refuses while any row (live or soft-deleted) holds a value in any of the four columns, because dropping them would lose data silently; restore the backup to go back
    /// further, or clear the values first. With every column empty it drops them, so Up, Down, Up works on a database that has rows.
    /// </summary>
    public partial class AddTaxReadyFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "HsCode",
                table: "Product",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SstCode",
                table: "Product",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SstRegNo",
                table: "Partner",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Tin",
                table: "Partner",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The check and the drops are ONE batch, on purpose: a script run by hand goes on to the next batch after an error (SSMS, sqlcmd without -b), so a refusal in a batch of its
            // own would be followed by the drops. THROW ends this batch, so none of them runs. The columns exist when it is compiled, so the script compiles as a whole.
            migrationBuilder.Sql("""
                SET XACT_ABORT ON; -- a run by hand that is stopped below leaves no open transaction
                IF EXISTS (SELECT 1 FROM [Product] WHERE [HsCode] IS NOT NULL OR [SstCode] IS NOT NULL)
                   OR EXISTS (SELECT 1 FROM [Partner] WHERE [Tin] IS NOT NULL OR [SstRegNo] IS NOT NULL)
                    THROW 50004, N'Tax-ready fields hold values (Product.HsCode, Product.SstCode, Partner.Tin or Partner.SstRegNo). Dropping the columns would lose them. Clear the values or restore the backup, then run the migration again.', 1;
                ALTER TABLE [Product] DROP COLUMN [HsCode], [SstCode];
                ALTER TABLE [Partner] DROP COLUMN [SstRegNo], [Tin];
                """);
        }
    }
}
