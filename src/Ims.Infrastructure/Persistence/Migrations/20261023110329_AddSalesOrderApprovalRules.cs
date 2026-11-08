using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ims.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// PL3-M3, data only, no schema change (the model is the same as the one before). Writes the two placeholder approval rules for sales orders (pilot slice PL3.4): from 5,000.00 a Supervisor
    /// (tier Standard) approves, from 20,000.00 the Owner (tier Large). The amounts are placeholders for the owner to confirm (an open owner item, like the purchase-order amounts); once sales is
    /// on they are ordinary rules the Administrator edits in Settings. Why a migration and not only the Seeder: the Seeder runs in Development or when <c>Database:MigrateOnStartup</c> is set, so a
    /// database deployed by a DBA's script would have no rule, and sales cannot be switched on without an active rule (a rule is written only while the module is on), so nothing could add one.
    /// The rows are written only while the table holds no rule of this type at all (DocType 6), so a rule an Administrator added, edited or switched off is never overruled and running it again
    /// adds nothing; the Seeder checks the same thing and the two agree on every value. The numbers below are a FROZEN statement of what this release ships (DocType 6 = SalesOrder, Tier 1 =
    /// Standard and 2 = Large), not the live enums. One statement, so a failed run leaves the table as it was. Down removes only the rows this migration wrote and nobody has touched since (the
    /// two exact values, written by no user, never revised, not deleted); a rule an Administrator edited, added or switched off stays.
    /// </summary>
    public partial class AddSalesOrderApprovalRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF NOT EXISTS (SELECT 1 FROM [ApprovalRule] WHERE [DocType] = 6)
                    INSERT INTO [ApprovalRule] ([DocType], [AmountThreshold], [QtyThreshold], [IsActive], [Tier], [PublicId], [REC_CREDATE], [REC_CREATOR], [REC_DELETED], [REC_REVDATE], [REC_REVISOR])
                    VALUES (6, 5000.00, NULL, 1, 1, NEWID(), SYSUTCDATETIME(), NULL, 0, NULL, NULL),
                           (6, 20000.00, NULL, 1, 2, NEWID(), SYSUTCDATETIME(), NULL, 0, NULL, NULL);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DELETE FROM [ApprovalRule]
                WHERE [DocType] = 6 AND [QtyThreshold] IS NULL AND [IsActive] = 1 AND [REC_DELETED] = 0 AND [REC_CREATOR] IS NULL AND [REC_REVDATE] IS NULL AND [REC_REVISOR] IS NULL
                  AND (([AmountThreshold] = 5000.00 AND [Tier] = 1) OR ([AmountThreshold] = 20000.00 AND [Tier] = 2));
                """);
        }
    }
}
