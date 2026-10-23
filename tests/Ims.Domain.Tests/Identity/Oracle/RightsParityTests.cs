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

            // What a role holds that the oracle does not know: the new permissions (modules.manage for the Admin, PL1.1; po.read and po.manage by the purchasing defaults, PL2.1; so.read and so.manage by the sales defaults, PL3.1), and for the Owner the large tier in place of document.approve.
            var added = defaults.Except(oracle).Order(StringComparer.Ordinal).ToArray();
            var removed = oracle.Except(defaults).Order(StringComparer.Ordinal).ToArray();

            var (expectedAdded, expectedRemoved) = role switch
            {
                Roles.Admin => ([Permissions.DocumentApproveLarge, Permissions.DocumentEditAny, Permissions.DocumentPostApproved, Permissions.ModulesManage, Permissions.PoManage, Permissions.PoRead, Permissions.RolesManage, Permissions.RolesRead,
                    Permissions.SoManage, Permissions.SoRead, Permissions.StockNegativeOverride], Array.Empty<string>()),
                Roles.Owner => ([Permissions.DocumentApproveLarge, Permissions.PoRead, Permissions.SoManage, Permissions.SoRead], [Permissions.DocumentApprove]),
                Roles.Supervisor => ([Permissions.DocumentEditAny, Permissions.DocumentPostApproved, Permissions.PoManage, Permissions.PoRead, Permissions.SoManage, Permissions.SoRead, Permissions.StockNegativeOverride], Array.Empty<string>()),
                Roles.WarehouseStaff => ([Permissions.PoRead, Permissions.SoRead], Array.Empty<string>()),
                Roles.Clerk => ([Permissions.PoManage, Permissions.PoRead, Permissions.SoManage, Permissions.SoRead], Array.Empty<string>()),
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
    public void TheApprovalDecisionByTierIsTheRoleRankDecision_ForEveryStoredTierRuleSetAndSystemRole_ExceptTheOwnerOnAnAdminRule()
    {
        // Routing and limit together, as the Approve handler asks them. The stored tier of the document varies on its own, apart from the rules in force now (they can
        // change between submit and approve), so a standard approver on a document stored as standard is refused when a rule naming the Owner is in force.
        // The one documented difference is Q5 (ADR roles-permissions 2.7, accepted by the owner): a rule that named the Administrator is a Large rule, and the Owner,
        // who holds the large tier, may now satisfy it, where the rank of role names kept it for the Administrator alone.
        var differences = new List<string>();
        foreach (var key in OracleKeys.RuleSetKeys())
        {
            var parts = ParseRuleParts(key[0]);
            var rules = ParseRules(key[0]);
            var amount = decimal.Parse(key[1], System.Globalization.CultureInfo.InvariantCulture);
            var adminRuleFired = parts.Any(p => p.Role == Roles.Admin && amount >= p.Threshold);
            foreach (var storedLarge in new[] { false, true })
            foreach (var role in SystemRoles)
            {
                var rights = SystemRoleDefaults.RightsFor(role, Me);
                var before = LegacyBehaviour.MayReview(role, storedLarge) && LegacyBehaviour.CanApprove(role, parts, amount);
                var now = ApprovalPolicy.MayReviewUnderRulesInForce(rights, storedLarge, rules, DocumentType.Receipt, 1m, amount);
                var documented = role == Roles.Owner && storedLarge && adminRuleFired;

                if (documented) Assert.True(!before && now, $"the Owner on an Admin rule: was {before}, now {now} ({key[0]}, amount {key[1]})");
                else if (before != now) differences.Add($"{role}, stored {(storedLarge ? "large" : "standard")}, rules {key[0]}, amount {key[1]}: was {before}, now {now}");
            }
        }

        Assert.True(differences.Count == 0, $"{differences.Count} difference(s):" + Environment.NewLine + string.Join(Environment.NewLine, differences.Take(15)));
    }

    [Fact]
    public void ASupervisorIsRefusedOnAStandardDocumentWhenARuleNamingTheOwnerIsInForce()
    {
        var rules = ParseRules("Owner@100");
        var supervisor = SystemRoleDefaults.RightsFor(Roles.Supervisor, Me);

        Assert.True(ApprovalPolicy.MayReview(supervisor, isLarge: false));
        Assert.False(ApprovalPolicy.MayReviewUnderRulesInForce(supervisor, isLarge: false, rules, DocumentType.Receipt, 1m, 500m));
    }

    [Fact]
    public void TheOwnerMaySatisfyARuleThatNamedTheAdministrator_AndASupervisorStillMayNot()
    {
        // Q5: an Admin-only rule is a Large rule. The Owner holds the large tier, so the document is theirs; a standard approver is not.
        var rules = ParseRules("Admin@100");
        Assert.All(rules, r => Assert.Equal(ApproverTier.Large, r.Tier));

        var owner = SystemRoleDefaults.RightsFor(Roles.Owner, Me);
        var supervisor = SystemRoleDefaults.RightsFor(Roles.Supervisor, Me);
        var admin = SystemRoleDefaults.RightsFor(Roles.Admin, Me);

        Assert.True(ApprovalPolicy.MayReviewUnderRulesInForce(owner, isLarge: true, rules, DocumentType.Receipt, 1m, 500m));
        Assert.True(ApprovalPolicy.MayReviewUnderRulesInForce(admin, isLarge: true, rules, DocumentType.Receipt, 1m, 500m));
        Assert.False(ApprovalPolicy.MayReviewUnderRulesInForce(supervisor, isLarge: true, rules, DocumentType.Receipt, 1m, 500m));
    }

    [Fact]
    public void AnApprovalTierIsNotLentByTheRoleName()
    {
        // A holder of only the standard tier never reviews a large document, whatever else they are called; the large tier alone never reviews a standard one.
        var standardOnly = new ActorRights(Me, EffectivePermissions.Compute([Permissions.DocumentApprove]));
        var largeOnly = new ActorRights(Me, EffectivePermissions.Compute([Permissions.DocumentApproveLarge]));

        Assert.False(ApprovalPolicy.MayReview(standardOnly, isLarge: true));
        Assert.True(ApprovalPolicy.MayReview(standardOnly, isLarge: false));
        Assert.True(ApprovalPolicy.MayReview(largeOnly, isLarge: true));
        Assert.False(ApprovalPolicy.MayReview(largeOnly, isLarge: false));
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
