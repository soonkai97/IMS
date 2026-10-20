using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ims.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Data only, no schema change (the model is the same as the one before). The permission <c>accounting.export</c> joins the catalog (ADR modules-and-tracking-dimensions section 8,
    /// pilot slice PL1.2) and the Administrator role has to hold every code of the catalog, so one row is added for it; the Owner role gets one too, because the Owner already sees cost
    /// and value (owner decision, pilot plan question 5). No other role holds it: whoever else should export gets it through the role editor. The rows are a FROZEN statement of what
    /// this release ships (the code string below, not the live catalog), one statement for the two system roles, so a failed run leaves the table as it was. Running it again adds nothing.
    /// The permission belongs to the accounting module: while that module is off the grant is kept but hidden, and the endpoint that uses it (a later slice) answers 404.
    /// Down removes every grant of the code, whichever role holds it: the previous release does not know the permission, so no role may keep a row for it.
    /// A person with one of the two roles gets the permission in the token at the next refresh (up to the token lifetime), as for every role change.
    /// </summary>
    public partial class GrantAccountingExport : Migration
    {
        private const string AccountingExport = "accounting.export";
        private const string Admin = "Admin";
        private const string Owner = "Owner";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($"""
                INSERT INTO [RolePermission] ([RoleId], [PermissionCode])
                SELECT r.[Id], N'{AccountingExport}'
                FROM [Role] r
                WHERE r.[Code] IN (N'{Admin}', N'{Owner}') AND r.[IsSystem] = 1 AND r.[REC_DELETED] = 0
                  AND NOT EXISTS (SELECT 1 FROM [RolePermission] p WHERE p.[RoleId] = r.[Id] AND p.[PermissionCode] = N'{AccountingExport}' AND p.[REC_DELETED] = 0);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($"DELETE FROM [RolePermission] WHERE [PermissionCode] = N'{AccountingExport}';");
        }
    }
}
