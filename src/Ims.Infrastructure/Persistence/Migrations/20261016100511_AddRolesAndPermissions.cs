using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ims.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Roles and their permissions move into the database (ADR roles-permissions 4.2, lean scope). Nothing that runs reads them yet: the API still decides by
    /// <c>User.Role</c> (the string stays, and is the source, until RP-4a), so what anyone may do is unchanged. One transaction, small tables (users, approval rules):
    ///   1. create <c>Role</c> and <c>RolePermission</c>;
    ///   2. seed the five system roles with a FROZEN copy of what they hold by default (the lists below, not the live catalog: the catalog changes after this
    ///      migration ships, and a fresh database gets the same rows as an upgraded one);
    ///   3. <c>User.RoleId</c>: added empty, filled from the role string, then NOT NULL with its foreign key. A user whose role string is not one of the five stops the
    ///      migration (nothing is guessed, nobody gets a lower or higher role by default);
    ///   4. <c>User.AllWarehouses</c>: true for the users whose role saw every warehouse (Admin, Owner), false for the rest;
    ///   5. <c>ApprovalRule.Tier</c>: Owner and Admin rules are Large, Supervisor, WarehouseStaff and Clerk rules Standard (what the old "a rule above a supervisor"
    ///      test did). A role string outside the five stops the migration and names the rule. <c>RoleRequired</c> stays until RP-4b. The active rules that only an
    ///      Administrator could satisfy (role Admin) become Large and so can now be satisfied by the Owner too (an owner decision, Q5): one audit row names them.
    /// Everything is rolled back when a step fails, so a failed run leaves the database as it was and can simply be run again. Down drops the new columns and tables;
    /// <c>User.Role</c> was never touched, so the old application works again.
    /// </summary>
    public partial class AddRolesAndPermissions : Migration
    {
        private const string Admin = "Admin";
        private const string Owner = "Owner";
        private const string Supervisor = "Supervisor";
        private const string WarehouseStaff = "WarehouseStaff";
        private const string Clerk = "Clerk";

        /// <summary>The system roles: code, name, description and the permissions each holds (ADR 2.1). Frozen: never read from the code of the application.</summary>
        private static readonly (string Code, string Name, string Description, string[] Permissions)[] SystemRoles =
        [
            (Admin, "Admin", "Manages users, settings and approval rules, and can do everything else.",
            [
                "users.read", "users.manage", "roles.read", "roles.manage", "settings.manage", "audit.read",
                "masterdata.read", "masterdata.manage", "partners.create",
                "document.create", "document.edit.any", "document.approve", "document.approve.large", "document.post", "document.post.approved", "document.reverse", "stock.negative.override",
                "stock.read", "reports.read", "cost.view", "valuation.view"
            ]),
            (Owner, "Owner", "Approves large documents and reads stock, reports, cost and the audit log.",
            [
                "masterdata.read", "document.approve.large", "stock.read", "reports.read", "audit.read", "cost.view", "valuation.view"
            ]),
            (Supervisor, "Supervisor", "Runs the warehouse: master data, approvals, posting and reversal.",
            [
                "masterdata.manage", "masterdata.read", "partners.create", "document.create", "document.edit.any", "document.approve", "document.post", "document.post.approved",
                "document.reverse", "stock.negative.override", "stock.read", "reports.read", "cost.view", "valuation.view"
            ]),
            (WarehouseStaff, "Warehouse staff", "Receives and issues stock and posts documents that need no approval.",
            [
                "masterdata.read", "document.create", "document.post", "stock.read"
            ]),
            (Clerk, "Clerk", "Prepares receipts and issues and adds partners.",
            [
                "masterdata.read", "partners.create", "document.create", "stock.read", "cost.view"
            ]),
        ];

        private const int Standard = 1;
        private const int Large = 2;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Role",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    IsSystem = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    REC_DELETED = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    REC_CREATOR = table.Column<int>(type: "int", nullable: true),
                    REC_CREDATE = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    REC_REVISOR = table.Column<int>(type: "int", nullable: true),
                    REC_REVDATE = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ROW_VERSION = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Role", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RolePermission",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RoleId = table.Column<int>(type: "int", nullable: false),
                    PermissionCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    REC_DELETED = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    REC_CREATOR = table.Column<int>(type: "int", nullable: true),
                    REC_CREDATE = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    REC_REVISOR = table.Column<int>(type: "int", nullable: true),
                    REC_REVDATE = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ROW_VERSION = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RolePermission", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RolePermission_Role_RoleId",
                        column: x => x.RoleId,
                        principalTable: "Role",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Role_Code",
                table: "Role",
                column: "Code",
                unique: true,
                filter: "[REC_DELETED] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_Role_Name",
                table: "Role",
                column: "Name",
                unique: true,
                filter: "[REC_DELETED] = 0");

            migrationBuilder.CreateIndex(
                name: "UX_Role_PublicId",
                table: "Role",
                column: "PublicId",
                unique: true)
                .Annotation("SqlServer:FillFactor", 90);

            migrationBuilder.CreateIndex(
                name: "IX_RolePermission_RoleId_PermissionCode",
                table: "RolePermission",
                columns: new[] { "RoleId", "PermissionCode" },
                unique: true,
                filter: "[REC_DELETED] = 0");

            SeedSystemRoles(migrationBuilder);

            // ----- User.RoleId: empty, filled from the role string, then required -----
            migrationBuilder.AddColumn<int>(
                name: "RoleId",
                table: "User",
                type: "int",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE u SET u.[RoleId] = r.[Id]
                FROM [User] u JOIN [Role] r ON r.[Code] = u.[Role] AND r.[REC_DELETED] = 0
                WHERE u.[RoleId] IS NULL;
                """);

            migrationBuilder.Sql("""
                DECLARE @unmapped nvarchar(2048) = (
                    SELECT TOP (1) CONCAT(N'User ', [Id], N' (', [PublicId], N') has the role ''', [Role], N''', which is not one of the five system roles. Fix the role, then run the migration again.')
                    FROM [User] WHERE [RoleId] IS NULL ORDER BY [Id]);
                IF @unmapped IS NOT NULL THROW 50001, @unmapped, 1;
                """);

            migrationBuilder.AlterColumn<int>(
                name: "RoleId",
                table: "User",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_User_RoleId",
                table: "User",
                column: "RoleId");

            migrationBuilder.AddForeignKey(
                name: "FK_User_Role_RoleId",
                table: "User",
                column: "RoleId",
                principalTable: "Role",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // ----- User.AllWarehouses: what the role saw before -----
            migrationBuilder.AddColumn<bool>(
                name: "AllWarehouses",
                table: "User",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.Sql($"UPDATE [User] SET [AllWarehouses] = 1 WHERE [Role] IN (N'{Admin}', N'{Owner}');");

            // ----- ApprovalRule.Tier: empty, filled from the role string, then required -----
            migrationBuilder.AddColumn<int>(
                name: "Tier",
                table: "ApprovalRule",
                type: "int",
                nullable: true);

            migrationBuilder.Sql($"""
                UPDATE [ApprovalRule] SET [Tier] = CASE [RoleRequired]
                    WHEN N'{Owner}' THEN {Large} WHEN N'{Admin}' THEN {Large}
                    WHEN N'{Supervisor}' THEN {Standard} WHEN N'{WarehouseStaff}' THEN {Standard} WHEN N'{Clerk}' THEN {Standard} END
                WHERE [Tier] IS NULL;
                """);

            migrationBuilder.Sql("""
                DECLARE @unmapped nvarchar(2048) = (
                    SELECT TOP (1) CONCAT(N'Approval rule ', [Id], N' (', [PublicId], N') names the role ''', [RoleRequired], N''', which is not one of the five system roles. Fix the rule, then run the migration again.')
                    FROM [ApprovalRule] WHERE [Tier] IS NULL ORDER BY [Id]);
                IF @unmapped IS NOT NULL THROW 50002, @unmapped, 1;
                """);

            migrationBuilder.AlterColumn<int>(
                name: "Tier",
                table: "ApprovalRule",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            // A rule that only an Administrator could satisfy is Large now, so the Owner can satisfy it too: say which ones (one row, written by the system).
            migrationBuilder.Sql($$"""
                INSERT INTO [AuditLogs] ([Actor], [Action], [EntityType], [EntityId], [After], [At])
                SELECT N'system', N'approval-rules.widened', N'ApprovalRule', N'bulk',
                       CONCAT(N'{"count":', COUNT(*), N',"ruleIds":["', STRING_AGG(CONVERT(nvarchar(36), [PublicId]), N'","'), N'"]}'), SYSUTCDATETIME()
                FROM [ApprovalRule] WHERE [RoleRequired] = N'{{Admin}}' AND [IsActive] = 1 AND [REC_DELETED] = 0
                HAVING COUNT(*) > 0;
                """);
        }

        /// <summary>The five system roles and, in a second statement, one row for every permission each holds.</summary>
        private static void SeedSystemRoles(MigrationBuilder migrationBuilder)
        {
            var roles = string.Join(",\n", SystemRoles.Select(r => $"(N'{r.Code}', N'{r.Name}', N'{r.Description}', 1, 1)"));
            migrationBuilder.Sql($"INSERT INTO [Role] ([Code], [Name], [Description], [IsSystem], [IsActive]) VALUES\n{roles};");

            var grants = string.Join(",\n", SystemRoles.SelectMany(r => r.Permissions.Select(p => $"(N'{r.Code}', N'{p}')")));
            migrationBuilder.Sql($"""
                INSERT INTO [RolePermission] ([RoleId], [PermissionCode])
                SELECT r.[Id], g.[PermissionCode] FROM (VALUES
                {grants}) AS g ([RoleCode], [PermissionCode])
                JOIN [Role] r ON r.[Code] = g.[RoleCode];
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_User_Role_RoleId",
                table: "User");

            migrationBuilder.DropTable(
                name: "RolePermission");

            migrationBuilder.DropTable(
                name: "Role");

            migrationBuilder.DropIndex(
                name: "IX_User_RoleId",
                table: "User");

            migrationBuilder.DropColumn(
                name: "AllWarehouses",
                table: "User");

            migrationBuilder.DropColumn(
                name: "RoleId",
                table: "User");

            migrationBuilder.DropColumn(
                name: "Tier",
                table: "ApprovalRule");
        }
    }
}
