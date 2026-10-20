using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ims.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Data only, no schema change (the model is the same as the one before). The permissions <c>po.read</c> and <c>po.manage</c> join the catalog (pilot slice 2, PL2.1; they belong to the
    /// <c>purchasing</c> module). The Administrator role has to hold every code of the catalog, and the other system roles get the defaults of the owner decision (pilot slice 2 plan, question 4):
    /// Supervisor and Clerk hold both, Owner and Warehouse staff hold <c>po.read</c> only. A role gets a row only while it holds what the permission requires (<c>po.read</c> needs
    /// <c>masterdata.read</c>; <c>po.manage</c> needs <c>po.read</c>, <c>cost.view</c> and <c>stock.read</c>), because an administrator may already have taken one from a system role on a
    /// populated database and a role without it would not be a closed set: such a role is left alone and can be given the rights in the role editor. Whoever holds <c>po.manage</c> here also
    /// gets <c>po.read</c> in the same statement, since both need <c>masterdata.read</c>. The rows are a FROZEN statement of what this release ships (the code strings below, not the live
    /// catalog), in one statement so a failed run leaves the table as it was. Running it again adds nothing. The permissions belong to the purchasing module: while it is off the grants are
    /// kept but hidden. Down removes every grant of both codes, whichever role holds them: the previous release does not know them, so no role may keep a row for them.
    /// A person with one of these roles gets the permissions in the token at the next refresh (up to the token lifetime), as for every role change.
    /// </summary>
    public partial class GrantPurchasingPermissions : Migration
    {
        private const string PoRead = "po.read";
        private const string PoManage = "po.manage";
        private const string MasterDataRead = "masterdata.read";
        private const string CostView = "cost.view";
        private const string StockRead = "stock.read";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($"""
                INSERT INTO [RolePermission] ([RoleId], [PermissionCode])
                SELECT r.[Id], g.[PermissionCode]
                FROM [Role] r
                JOIN (VALUES
                    (N'Admin', N'{PoRead}'), (N'Owner', N'{PoRead}'), (N'Supervisor', N'{PoRead}'), (N'WarehouseStaff', N'{PoRead}'), (N'Clerk', N'{PoRead}'),
                    (N'Admin', N'{PoManage}'), (N'Supervisor', N'{PoManage}'), (N'Clerk', N'{PoManage}')
                ) g ([RoleCode], [PermissionCode]) ON g.[RoleCode] = r.[Code]
                WHERE r.[IsSystem] = 1 AND r.[REC_DELETED] = 0
                  AND EXISTS (SELECT 1 FROM [RolePermission] m WHERE m.[RoleId] = r.[Id] AND m.[PermissionCode] = N'{MasterDataRead}' AND m.[REC_DELETED] = 0)
                  AND (g.[PermissionCode] = N'{PoRead}'
                       OR (EXISTS (SELECT 1 FROM [RolePermission] c WHERE c.[RoleId] = r.[Id] AND c.[PermissionCode] = N'{CostView}' AND c.[REC_DELETED] = 0)
                           AND EXISTS (SELECT 1 FROM [RolePermission] s WHERE s.[RoleId] = r.[Id] AND s.[PermissionCode] = N'{StockRead}' AND s.[REC_DELETED] = 0)))
                  AND NOT EXISTS (SELECT 1 FROM [RolePermission] p WHERE p.[RoleId] = r.[Id] AND p.[PermissionCode] = g.[PermissionCode] AND p.[REC_DELETED] = 0);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($"DELETE FROM [RolePermission] WHERE [PermissionCode] IN (N'{PoRead}', N'{PoManage}');");
        }
    }
}
