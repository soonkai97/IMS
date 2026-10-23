using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ims.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Data only, no schema change (the model is the same as the one before). The permissions <c>so.read</c> and <c>so.manage</c> join the catalog (pilot slice 3, PL3.1; they belong to the
    /// <c>sales</c> module). The Administrator role has to hold every code of the catalog, and the other system roles get the defaults of the pilot slice 3 plan: Owner, Supervisor and Clerk hold
    /// both, Warehouse staff hold <c>so.read</c> only (they deliver against an order without seeing its prices). A role gets a row only while it holds what the permission requires
    /// (<c>so.read</c> needs <c>masterdata.read</c>; <c>so.manage</c> needs <c>so.read</c> only, a selling price is not cost), because an administrator may already have taken one from a system
    /// role on a populated database and a role without it would not be a closed set: such a role is left alone and can be given the rights in the role editor. Whoever holds <c>so.manage</c> here
    /// also gets <c>so.read</c> in the same statement, since both need <c>masterdata.read</c>. The rows are a FROZEN statement of what this release ships (the code strings below, not the live
    /// catalog), in one statement so a failed run leaves the table as it was. Running it again adds nothing. The permissions belong to the sales module: while it is off the grants are kept but
    /// hidden. Down removes every grant of both codes, whichever role holds them: the previous release does not know them, so no role may keep a row for them.
    /// Deploy note: a custom role that holds every older permission no longer equals the system roles, which now hold more; a person with one of these roles gets the permissions in the token at
    /// the next refresh (up to the token lifetime, 5 minutes), as for every role change; the <c>sales</c> module stays off in production until the slice is complete, nothing sits behind it before that.
    /// </summary>
    public partial class GrantSalesPermissions : Migration
    {
        private const string SoRead = "so.read";
        private const string SoManage = "so.manage";
        private const string MasterDataRead = "masterdata.read";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($"""
                INSERT INTO [RolePermission] ([RoleId], [PermissionCode])
                SELECT r.[Id], g.[PermissionCode]
                FROM [Role] r
                JOIN (VALUES
                    (N'Admin', N'{SoRead}'), (N'Owner', N'{SoRead}'), (N'Supervisor', N'{SoRead}'), (N'WarehouseStaff', N'{SoRead}'), (N'Clerk', N'{SoRead}'),
                    (N'Admin', N'{SoManage}'), (N'Owner', N'{SoManage}'), (N'Supervisor', N'{SoManage}'), (N'Clerk', N'{SoManage}')
                ) g ([RoleCode], [PermissionCode]) ON g.[RoleCode] = r.[Code]
                WHERE r.[IsSystem] = 1 AND r.[REC_DELETED] = 0
                  AND EXISTS (SELECT 1 FROM [RolePermission] m WHERE m.[RoleId] = r.[Id] AND m.[PermissionCode] = N'{MasterDataRead}' AND m.[REC_DELETED] = 0)
                  AND NOT EXISTS (SELECT 1 FROM [RolePermission] p WHERE p.[RoleId] = r.[Id] AND p.[PermissionCode] = g.[PermissionCode] AND p.[REC_DELETED] = 0);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($"DELETE FROM [RolePermission] WHERE [PermissionCode] IN (N'{SoRead}', N'{SoManage}');");
        }
    }
}
