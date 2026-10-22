using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ims.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Part B of the removal of the accounting module (owner decision 2026-10-22): the four tax-ready columns <c>Product.HsCode</c>, <c>Product.SstCode</c>, <c>Partner.Tin</c> and
    /// <c>Partner.SstRegNo</c> (added by <c>AddTaxReadyFields</c>) are dropped. Nothing in the application reads or writes them any more. Dropping a nullable column is a catalog change
    /// on SQL Server, whatever the table size. Up drops the columns with any value still held in them, without a check and without an audit row: the module that could switch the fields on was dormant and never used in a real
    /// installation, and a refusal would only block the release for an installation that cannot use the values. The audit log is append-only, so an old RowCreated or RowUpdated row of a product or partner that had a value keeps it in its Before/After JSON (development data only). Down adds the four columns back, nullable and EMPTY (same names and sizes as
    /// <c>AddTaxReadyFields</c>); the dropped values are not restored, restore the backup for that. After Down, <c>AddTaxReadyFields</c> can still be taken down as before, because the columns are empty.
    /// </summary>
    public partial class RemoveTaxReadyFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HsCode",
                table: "Product");

            migrationBuilder.DropColumn(
                name: "SstCode",
                table: "Product");

            migrationBuilder.DropColumn(
                name: "SstRegNo",
                table: "Partner");

            migrationBuilder.DropColumn(
                name: "Tin",
                table: "Partner");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
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
    }
}
