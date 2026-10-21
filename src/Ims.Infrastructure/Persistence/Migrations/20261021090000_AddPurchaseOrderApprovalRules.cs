using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ims.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Data only, no schema change (the model is the same as the one before). Writes the two placeholder approval rules for purchase orders (pilot slice 2, PL2.3): from 5,000.00 a Supervisor
    /// (tier Standard) approves, from 20,000.00 the Owner (tier Large). The amounts are placeholders for the owner to confirm; once purchasing is on they are ordinary rules the Administrator edits
    /// in Settings. Why a migration and not only the Seeder: the Seeder runs in Development or when <c>Database:MigrateOnStartup</c> is set, so a database deployed by a DBA's script would have no rule,
    /// every priced order would be released with no approver, and (since purchasing is switched on only with an active rule and a rule is written only while it is on) nothing could add one. The rows
    /// are written only while the table holds no rule of this type at all (DocType 5), so a rule an Administrator added, edited or switched off is never overruled and running it again adds nothing;
    /// the Seeder checks the same thing and the two agree on every value. The numbers below are a FROZEN statement of what this release ships (DocType 5 = PurchaseOrder, Tier 1 = Standard and
    /// 2 = Large), not the live enums. One statement, so a failed run leaves the table as it was. Down does nothing: the rules may have been edited, and the previous release ignores them
    /// (they are hidden while the module is off).
    /// </summary>
    public partial class AddPurchaseOrderApprovalRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF NOT EXISTS (SELECT 1 FROM [ApprovalRule] WHERE [DocType] = 5)
                    INSERT INTO [ApprovalRule] ([DocType], [AmountThreshold], [QtyThreshold], [IsActive], [Tier], [PublicId], [REC_CREDATE], [REC_CREATOR], [REC_DELETED], [REC_REVDATE], [REC_REVISOR])
                    VALUES (5, 5000.00, NULL, 1, 1, NEWID(), SYSUTCDATETIME(), NULL, 0, NULL, NULL),
                           (5, 20000.00, NULL, 1, 2, NEWID(), SYSUTCDATETIME(), NULL, 0, NULL, NULL);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
