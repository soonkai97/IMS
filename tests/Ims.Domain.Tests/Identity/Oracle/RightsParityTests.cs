using static Ims.Domain.Tests.Identity.Oracle.OracleKeys;

namespace Ims.Domain.Tests.Identity.Oracle;

/// <summary>
/// The "safe default" proof of RP-1: the new types (<see cref="SystemRoleDefaults"/>, <see cref="ActorRights"/> and the policies that take it) answer
/// every question of the oracle exactly as the role-keyed code did, for the five system roles. Three differences are documented and asserted
/// explicitly: the Owner approves by <c>document.approve.large</c>, the new permissions exist, and two standalone answers of
/// <see cref="PostingAuthority"/> only hold together with <c>document.post</c>.
/// </summary>
public class RightsParityTests
{
    [Fact]
    public void TheDocumentPoliciesAnswerEveryQuestionOfTheOracle()
    {
        var differences = OracleTables.DocumentActions.Differences(NewBehaviour.DocumentAsFrozen);

        Assert.True(differences.Count == 0, $"{differences.Count} answer(s) differ from the oracle:\n" + string.Join('\n', differences.Take(15)));
    }

    [Fact]
    public void TheRoleRulesAnswerAsTheOracleDoes()
    {
        var differences = OracleTables.Roles.Differences(NewBehaviour.Role);

        Assert.True(differences.Count == 0, $"{differences.Count} answer(s) differ from the oracle:\n" + string.Join('\n', differences.Take(15)));
    }

    [Fact]
    public void ThePermissionsOfEachRoleAreTheOraclesExceptTheDocumentedChanges()
    {
        foreach (var row in OracleTables.Roles.Rows())
        {
            var role = row["role"];
            var oracle = row["permissions"].Split(';', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
            var defaults = SystemRoleDefaults.For(role).ToHashSet();

            // What a role holds that the oracle does not know: the six new permissions, and for the Owner the large tier in place of document.approve.
            var added = defaults.Except(oracle).Order(StringComparer.Ordinal).ToArray();
            var removed = oracle.Except(defaults).Order(StringComparer.Ordinal).ToArray();

            var (expectedAdded, expectedRemoved) = role switch
            {
                Roles.Admin => ([Permissions.DocumentApproveLarge, Permissions.DocumentEditAny, Permissions.DocumentPostApproved, Permissions.RolesManage, Permissions.RolesRead,
                    Permissions.StockNegativeOverride], Array.Empty<string>()),
                Roles.Owner => ([Permissions.DocumentApproveLarge], [Permissions.DocumentApprove]),
                Roles.Supervisor => ([Permissions.DocumentEditAny, Permissions.DocumentPostApproved, Permissions.StockNegativeOverride], Array.Empty<string>()),
                _ => (Array.Empty<string>(), Array.Empty<string>())
            };

            Assert.Equal(expectedAdded, added);
            Assert.Equal(expectedRemoved, removed);
        }
    }

    [Fact]
    public void AnyoneWhoCouldApproveBeforeCanReviewNow_ByTheTierTheirRoleHolds()
    {
        // The oracle's document.approve is, for a system role, "reviews standard documents" (Supervisor, Admin) or "reviews large documents" (Owner, Admin).
        foreach (var row in OracleTables.Roles.Rows())
        {
            var rights = SystemRoleDefaults.RightsFor(row["role"], Me);
            var couldApprove = row["permissions"].Split(';').Contains(Permissions.DocumentApprove);

            Assert.Equal(couldApprove, rights.Has(Permissions.DocumentApprove) || rights.Has(Permissions.DocumentApproveLarge));
        }
    }

    [Fact]
    public void PostingAuthorityAgreesWithTheOracleForEveryoneWhoCanPost()
    {
        // The role-keyed answers were standalone and true for roles that cannot post at all (Owner, Clerk, an unknown role), where they never mattered:
        // the post endpoint refuses first. The new permissions require document.post, so the comparison is made for those who hold it.
        foreach (var row in OracleTables.Roles.Rows())
        {
            var rights = SystemRoleDefaults.RightsFor(row["role"], Me);
            var canPost = row["permissions"].Split(';').Contains(Permissions.DocumentPost);
            if (!canPost)
            {
                Assert.False(rights.Has(Permissions.DocumentPost), row["role"]);
                continue;
            }

            Assert.Equal(row["postsApprovalRequired"] == "1", PostingAuthority.MayPostApprovalRequiredDocument(rights));
            Assert.Equal(row["overridesNegativeStock"] == "1", PostingAuthority.MayOverrideNegativeStock(rights));
        }
    }

    [Fact]
    public void NobodyWhoCannotPostHoldsAPostingExtra()
    {
        foreach (var role in RolesWithUnknown)
        {
            var rights = SystemRoleDefaults.RightsFor(role, Me);
            if (rights.Has(Permissions.DocumentPost)) continue;

            Assert.False(PostingAuthority.MayPostApprovalRequiredDocument(rights), role);
            Assert.False(PostingAuthority.MayOverrideNegativeStock(rights), role);
        }
    }

    [Fact]
    public void AnUnknownRoleHoldsNothing()
    {
        var rights = SystemRoleDefaults.RightsFor("Nobody", Me);

        Assert.Empty(rights.Effective.Codes);
        Assert.Empty(SystemRoleDefaults.For("Nobody"));
    }
}
