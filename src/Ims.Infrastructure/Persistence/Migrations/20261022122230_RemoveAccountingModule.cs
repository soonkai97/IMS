using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ims.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Data only, no schema change (the model is the same as the one before). The accounting module is removed from the code (owner decision 2026-10-22, part A of the removal): the
    /// permission <c>accounting.export</c> leaves the catalog and the module <c>accounting</c> leaves the module catalog, so nothing reads their rows any more. This deletes every
    /// <c>RolePermission</c> row of the code <c>accounting.export</c> (whichever role holds it, including the hidden grants an off module kept) and the module switch row
    /// <c>Module.accounting</c> of <c>SystemSetting</c>, in one batch so a failed run leaves both tables as they were. Both are hard deletes, like the Down of
    /// <c>GrantAccountingExport</c>: a permission that is not in the catalog must not stay in a role (the role editor would refuse the role at its next save). Running it again deletes nothing.
    /// The four tax columns of Product and Partner are NOT touched here (part B). The code strings below are FROZEN (not the live catalog, which no longer lists them).
    /// Down does nothing, on purpose: the deleted grants and the switch are not recorded anywhere, so they cannot be put back, and the previous release treats a missing grant as "not granted"
    /// and a missing switch as "module off", which is what the removal leaves anyway. The older <c>GrantAccountingExport</c> and <c>AddTaxReadyFields</c> stay in the chain as history.
    /// A person who still holds the permission in a token loses nothing real: no endpoint uses it, and the token is replaced at the next refresh.
    /// </summary>
    public partial class RemoveAccountingModule : Migration
    {
        private const string AccountingExport = "accounting.export";
        private const string AccountingSwitch = "Module.accounting";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($"""
                DELETE FROM [RolePermission] WHERE [PermissionCode] = N'{AccountingExport}';
                DELETE FROM [SystemSetting] WHERE [Key] = N'{AccountingSwitch}';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Intentionally empty: see the summary above (the deleted rows are not restored).
        }
    }
}
