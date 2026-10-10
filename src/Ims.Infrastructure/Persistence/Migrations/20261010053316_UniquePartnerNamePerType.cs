using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ims.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class UniquePartnerNamePerType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Databases that already hold the same name twice (same type, live rows) would fail the unique index below. Keep the oldest
            // partner as it is and tag each later one with its id, so nothing is lost and documents keep pointing at the same rows.
            migrationBuilder.Sql(@"
UPDATE p
SET p.Name = LEFT(p.Name, 170) + N' (duplicate ' + CAST(p.Id AS nvarchar(20)) + N')'
FROM [Partner] p
WHERE p.[REC_DELETED] = 0
  AND EXISTS (SELECT 1 FROM [Partner] o WHERE o.[REC_DELETED] = 0 AND o.[Type] = p.[Type] AND o.[Name] = p.[Name] AND o.[Id] < p.[Id]);");

            migrationBuilder.DropIndex(
                name: "IX_Partner_Type_Name",
                table: "Partner");

            migrationBuilder.CreateIndex(
                name: "IX_Partner_Type_Name",
                table: "Partner",
                columns: new[] { "Type", "Name" },
                unique: true,
                filter: "[REC_DELETED] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Partner_Type_Name",
                table: "Partner");

            migrationBuilder.CreateIndex(
                name: "IX_Partner_Type_Name",
                table: "Partner",
                columns: new[] { "Type", "Name" });
        }
    }
}
