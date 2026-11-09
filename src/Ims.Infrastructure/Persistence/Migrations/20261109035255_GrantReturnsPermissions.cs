using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ims.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// PL3-M6 (pilot slice PL3.15): data only, no schema change (the model is the same as the one before). The permission <c>returns.create</c> joins the catalog; it belongs to the <c>returns</c> module.
    /// The Administrator role has to hold every code of the catalog, and the other system roles that can create a stock document get it by default: Supervisor, Warehouse staff and Clerk (the Owner holds no
    /// <c>document.create</c>, so it gets none). A role gets a row only while it holds what the permission requires (<c>document.create</c> and <c>stock.read</c>), because an administrator may already have taken one
    /// from a system role on a populated database and a role without it would not be a closed set: such a role is left alone and can be given the right in the role editor. The rows are a FROZEN statement
    /// of what this release ships (the code string below, not the live catalog), in one statement so a failed run leaves the table as it was. Running it again adds nothing. The permission belongs to the returns
    /// module: while it is off the grants are kept but hidden. Down removes every grant of the code, whichever role holds it: the previous release does not know it, so no role may keep a row for it.
    /// Both statements begin with <c>SET QUOTED_IDENTIFIER ON</c>: <c>RolePermission</c> has a filtered unique index and <c>sqlcmd</c> starts with the setting OFF.
    /// </summary>
    public partial class GrantReturnsPermissions : Migration
    {
        private const string ReturnsCreate = "returns.create";
        private const string DocumentCreate = "document.create";
        private const string StockRead = "stock.read";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($"""
                SET QUOTED_IDENTIFIER ON;
                INSERT INTO [RolePermission] ([RoleId], [PermissionCode])
                SELECT r.[Id], g.[PermissionCode]
                FROM [Role] r
                JOIN (VALUES
                    (N'Admin', N'{ReturnsCreate}'), (N'Supervisor', N'{ReturnsCreate}'), (N'WarehouseStaff', N'{ReturnsCreate}'), (N'Clerk', N'{ReturnsCreate}')
                ) g ([RoleCode], [PermissionCode]) ON g.[RoleCode] = r.[Code]
                WHERE r.[IsSystem] = 1 AND r.[REC_DELETED] = 0
                  AND EXISTS (SELECT 1 FROM [RolePermission] c WHERE c.[RoleId] = r.[Id] AND c.[PermissionCode] = N'{DocumentCreate}' AND c.[REC_DELETED] = 0)
                  AND EXISTS (SELECT 1 FROM [RolePermission] s WHERE s.[RoleId] = r.[Id] AND s.[PermissionCode] = N'{StockRead}' AND s.[REC_DELETED] = 0)
                  AND NOT EXISTS (SELECT 1 FROM [RolePermission] p WHERE p.[RoleId] = r.[Id] AND p.[PermissionCode] = g.[PermissionCode] AND p.[REC_DELETED] = 0);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($"SET QUOTED_IDENTIFIER ON; DELETE FROM [RolePermission] WHERE [PermissionCode] = N'{ReturnsCreate}';");
        }
    }
}
