using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ims.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Data only, no schema change (the model is the same as the one before). The permission <c>modules.manage</c> joins the catalog (ADR modules-and-tracking-dimensions 4.6)
    /// and the Administrator role has to hold every code of the catalog, so one row is added for it. Nothing else holds it: whoever else should turn modules on or off gets it
    /// through the role editor. The rows are a FROZEN statement of what this release ships (the code string below, not the live catalog). Running it again adds nothing. Up and
    /// Down are one statement each, so a failed run leaves the table as it was.
    /// Down removes every grant of the code, whichever role holds it: the previous release does not know the permission, so no role may keep a row for it.
    /// A person with the Administrator role gets the permission in the token at the next refresh (up to the token lifetime), as for every role change.
    /// </summary>
    public partial class GrantModulesManage : Migration
    {
        private const string ModulesManage = "modules.manage";
        private const string Admin = "Admin";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($"""
                INSERT INTO [RolePermission] ([RoleId], [PermissionCode])
                SELECT r.[Id], N'{ModulesManage}'
                FROM [Role] r
                WHERE r.[Code] = N'{Admin}' AND r.[IsSystem] = 1 AND r.[REC_DELETED] = 0
                  AND NOT EXISTS (SELECT 1 FROM [RolePermission] p WHERE p.[RoleId] = r.[Id] AND p.[PermissionCode] = N'{ModulesManage}' AND p.[REC_DELETED] = 0);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($"DELETE FROM [RolePermission] WHERE [PermissionCode] = N'{ModulesManage}';");
        }
    }
}
