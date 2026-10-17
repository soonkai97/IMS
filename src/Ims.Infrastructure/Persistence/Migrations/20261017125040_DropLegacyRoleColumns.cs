using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ims.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// A role is data (ADR roles-permissions 4.2 step 6, RP-4b): the two legacy role strings go, <c>User.RoleId</c> and <c>ApprovalRule.Tier</c> are the only record. Nothing
    /// that runs reads the strings since RP-4a (the rights come from the role row's permissions through the token claims), so no right moves. One transaction, small tables:
    ///   1. stop if any user of a system role has a <c>Role</c> text that is not exactly the code of the role row its <c>RoleId</c> points at (a custom role has no text
    ///      of its own: Down writes WarehouseStaff there, and a second run must not be stopped by it). The row has decided the person's rights since RP-4a, but the
    ///      text was what the list showed and the old release decided by, so a difference is a manual edit someone must look at: nothing is guessed, nobody silently changes role;
    ///   2. one audit row names the active approval rules that name the Administrator: they are Large rules (since the RP-2 migration) and from this release the Owner can
    ///      satisfy them too, because the approval check no longer ranks role names (an owner decision, Q5);
    ///   3. drop <c>User.Role</c> and <c>ApprovalRule.RoleRequired</c>.
    /// A failed run leaves the database as it was and can simply be run again. Down puts both columns back from the rows (a user in a custom role gets WarehouseStaff, the
    /// least privilege; a Standard rule gets Supervisor and a Large rule Owner), so the previous release works again.
    /// </summary>
    public partial class DropLegacyRoleColumns : Migration
    {
        private const string Admin = "Admin";
        private const string WarehouseStaff = "WarehouseStaff";
        private const string Supervisor = "Supervisor";
        private const string Owner = "Owner";
        private const int Large = 2;

        /// <summary>The column equals the text exactly: same case, same length (the collation of the database ignores both case and trailing spaces).</summary>
        private static string Exactly(string column, string text) =>
            $"({column} COLLATE Latin1_General_BIN2 = N'{text}' AND DATALENGTH({column}) = {text.Length * sizeof(char)})";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DECLARE @differs nvarchar(2048) = (
                    SELECT TOP (1) CONCAT(N'User ', u.[Id], N' (', u.[PublicId], N') has the role text ''', u.[Role], N''' but its role row is ''', r.[Code], N'''. Make them agree, then run the migration again.')
                    FROM [User] u JOIN [Role] r ON r.[Id] = u.[RoleId]
                    WHERE r.[IsSystem] = 1 AND NOT (u.[Role] COLLATE Latin1_General_BIN2 = r.[Code] COLLATE Latin1_General_BIN2 AND DATALENGTH(u.[Role]) = DATALENGTH(r.[Code]))
                    ORDER BY u.[Id]);
                IF @differs IS NOT NULL THROW 50003, @differs, 1;
                """);

            migrationBuilder.Sql($$"""
                INSERT INTO [AuditLogs] ([Actor], [Action], [EntityType], [EntityId], [After], [At])
                SELECT N'system', N'approval-rules.widened', N'ApprovalRule', N'bulk',
                       CONCAT(N'{"count":', COUNT(*), N',"ruleIds":["', STRING_AGG(CONVERT(nvarchar(max), [PublicId]), N'","'), N'"]}'), SYSUTCDATETIME()
                FROM [ApprovalRule] WHERE {{Exactly("[RoleRequired]", Admin)}} AND [IsActive] = 1 AND [REC_DELETED] = 0
                HAVING COUNT(*) > 0;
                """);

            migrationBuilder.DropColumn(
                name: "Role",
                table: "User");

            migrationBuilder.DropColumn(
                name: "RoleRequired",
                table: "ApprovalRule");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Role",
                table: "User",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.Sql($"UPDATE u SET u.[Role] = CASE WHEN r.[IsSystem] = 1 THEN r.[Code] ELSE N'{WarehouseStaff}' END FROM [User] u JOIN [Role] r ON r.[Id] = u.[RoleId];");

            migrationBuilder.AlterColumn<string>(
                name: "Role",
                table: "User",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(32)",
                oldMaxLength: 32,
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RoleRequired",
                table: "ApprovalRule",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.Sql($"UPDATE [ApprovalRule] SET [RoleRequired] = CASE WHEN [Tier] = {Large} THEN N'{Owner}' ELSE N'{Supervisor}' END;");

            migrationBuilder.AlterColumn<string>(
                name: "RoleRequired",
                table: "ApprovalRule",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(32)",
                oldMaxLength: 32,
                oldNullable: true);
        }
    }
}
