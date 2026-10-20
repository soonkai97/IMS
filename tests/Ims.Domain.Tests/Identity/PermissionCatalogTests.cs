using System.Reflection;
using System.Text.RegularExpressions;

namespace Ims.Domain.Tests.Identity;

/// <summary>The permission catalog is the source of truth for what can be granted (ADR roles-permissions 2.1, 6.1): complete, unique, well formed, pinned.</summary>
public partial class PermissionCatalogTests
{
    [GeneratedRegex(@"^[a-z]+(\.[a-z]+)+$")]
    private static partial Regex CodePattern();

    private static IReadOnlyList<string> Constants { get; } = typeof(Permissions).GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
        .Where(f => f is { IsLiteral: true } && f.FieldType == typeof(string))
        .Select(f => (string)f.GetRawConstantValue()!).ToList();

    [Fact]
    public void TheCatalogHasTheTwentyThreePermissions_TheTwentyOneOfTheAdr_ModulesManage_AndAccountingExport()
    {
        Assert.Equal(23, PermissionCatalog.All.Count);
    }

    [Fact]
    public void EveryPermissionConstantIsInTheCatalogExactlyOnce_AndNothingElseIs()
    {
        Assert.Equal(Constants.Order(StringComparer.Ordinal), PermissionCatalog.Codes.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void TheCodesAreUnique()
    {
        var duplicates = PermissionCatalog.Codes.GroupBy(c => c).Where(g => g.Count() > 1).Select(g => g.Key);

        Assert.Empty(duplicates);
    }

    [Fact]
    public void TheCodesAreLowerCaseDottedWords_AtMostSixtyFourCharacters()
    {
        var offenders = PermissionCatalog.Codes.Where(c => !CodePattern().IsMatch(c) || c.Length > Limits.PermissionCodeMaxLength);

        Assert.Empty(offenders);
    }

    [Fact]
    public void EveryEntryHasAGroupAndAOneSentenceDescription()
    {
        foreach (var entry in PermissionCatalog.All)
        {
            Assert.True(Enum.IsDefined(entry.Group), entry.Code);
            Assert.False(string.IsNullOrWhiteSpace(entry.Description), entry.Code);
            Assert.EndsWith(".", entry.Description);
            Assert.Equal(1, entry.Description.Count(c => c == '.'));
        }
    }

    [Fact]
    public void TheGroupsFollowTheCatalogOrder()
    {
        // The role editor lists the groups in this order and the catalog order inside a group.
        Assert.Equal(PermissionCatalog.All.Select(p => p.Group).Order(), PermissionCatalog.All.Select(p => p.Group));
    }

    [Fact]
    public void RequiresPointsToRealCodes_NeverToItself_AndHasNoCycle()
    {
        foreach (var entry in PermissionCatalog.All)
        {
            Assert.All(entry.Requires, required =>
            {
                Assert.True(PermissionCatalog.IsKnown(required), $"{entry.Code} requires unknown {required}");
                Assert.NotEqual(entry.Code, required);
            });
            Assert.False(ReachesItself(entry.Code), $"{entry.Code} requires itself through a chain");
        }
    }

    private static bool ReachesItself(string code)
    {
        var seen = new HashSet<string>();
        var pending = new Stack<string>(PermissionCatalog.Find(code)!.Requires);
        while (pending.Count > 0)
        {
            var next = pending.Pop();
            if (next == code) return true;
            if (seen.Add(next)) foreach (var required in PermissionCatalog.Find(next)!.Requires) pending.Push(required);
        }
        return false;
    }

    [Fact]
    public void TheDangerousPermissions_ArePinned()
    {
        // A change is a visible diff: granting these needs an administrator and a reason.
        Assert.Equal(
            new[]
            {
                Permissions.AuditRead, Permissions.CostView, Permissions.DocumentReverse, Permissions.ModulesManage, Permissions.RolesManage, Permissions.SettingsManage,
                Permissions.StockNegativeOverride, Permissions.UsersManage, Permissions.ValuationView
            }.Order(StringComparer.Ordinal),
            PermissionCatalog.All.Where(p => p.IsDangerous).Select(p => p.Code).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void TheAdministrabilityCore_IsPinned()
    {
        Assert.Equal(
            new[] { Permissions.RolesManage, Permissions.RolesRead, Permissions.UsersManage, Permissions.UsersRead }.Order(StringComparer.Ordinal),
            PermissionCatalog.All.Where(p => p.AdminCore).Select(p => p.Code).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void FindAndIsKnown_AgreeWithTheCatalog()
    {
        Assert.Equal(Permissions.CostView, PermissionCatalog.Find(Permissions.CostView)!.Code);
        Assert.Null(PermissionCatalog.Find("cost.edit"));
        Assert.False(PermissionCatalog.IsKnown("COST.VIEW"));
    }

    [Fact]
    public void ModulesManage_IsDangerousAdministration_OutsideTheAdministrabilityCore_AndOnlyTheAdminHoldsIt()
    {
        var entry = PermissionCatalog.Find(Permissions.ModulesManage)!;

        Assert.Equal(PermissionGroup.Administration, entry.Group);
        Assert.True(entry.IsDangerous);
        Assert.False(entry.AdminCore); // the last-administrator guard is about users and roles, not about modules
        Assert.Empty(entry.Requires);
        Assert.Contains(Permissions.ModulesManage, SystemRoleDefaults.For(Roles.Admin));
        foreach (var role in Roles.All.Where(r => r != Roles.Admin))
            Assert.DoesNotContain(Permissions.ModulesManage, SystemRoleDefaults.For(role));
    }

    [Fact]
    public void AccountingExport_BelongsToTheAccountingModule_RequiresCostView_IsNotDangerous_AndIsHeldByTheAdminAndTheOwnerOnly()
    {
        var entry = PermissionCatalog.Find(Permissions.AccountingExport)!;

        Assert.Equal(ModuleCodes.Accounting, entry.Module);
        Assert.Equal([Permissions.CostView], entry.Requires); // the file carries cost, so whoever holds it already holds the dangerous cost.view
        // Decision (PL1.2): not dangerous. The ADR does not flag it; it reads, changes nothing, is audited (PL1.6), and cannot be held without cost.view, which already is dangerous, so it
        // gives no reach beyond what the dangerous cost.view grants. Pinned with TheDangerousPermissions_ArePinned, so flagging it later is a visible one-line diff.
        Assert.False(entry.IsDangerous);
        Assert.False(entry.AdminCore);
        foreach (var role in new[] { Roles.Admin, Roles.Owner })
            Assert.Contains(Permissions.AccountingExport, SystemRoleDefaults.For(role));
        foreach (var role in new[] { Roles.Supervisor, Roles.WarehouseStaff, Roles.Clerk })
            Assert.DoesNotContain(Permissions.AccountingExport, SystemRoleDefaults.For(role));
    }

    [Fact]
    public void EveryModuleOfAPermissionIsAModuleOfTheCatalog_AndCoreEntriesHaveNone()
    {
        foreach (var entry in PermissionCatalog.All.Where(p => p.Module is not null))
            Assert.True(ModuleCatalog.IsKnown(entry.Module!), $"{entry.Code} names unknown module {entry.Module}");
        Assert.Equal([Permissions.AccountingExport], PermissionCatalog.All.Where(p => p.Module is not null).Select(p => p.Code)); // pinned: adding one is a visible diff
    }

    // ----- the defaults of the system roles -----
    [Fact]
    public void EverySystemRoleDefault_IsInTheCatalog_AndClosedUnderRequires()
    {
        foreach (var role in Roles.All)
        {
            var held = SystemRoleDefaults.For(role).ToHashSet();

            Assert.All(held, code => Assert.True(PermissionCatalog.IsKnown(code), $"{role}: {code}"));
            foreach (var code in held)
                Assert.All(PermissionCatalog.Find(code)!.Requires, required => Assert.True(held.Contains(required), $"{role} holds {code} without {required}"));
        }
    }

    [Fact]
    public void TheAdminHoldsEveryPermissionOfTheCatalog()
    {
        Assert.Equal(PermissionCatalog.Codes.Order(StringComparer.Ordinal), SystemRoleDefaults.For(Roles.Admin).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void OnlyTheAdminHoldsTheAdministrabilityCore()
    {
        var core = PermissionCatalog.All.Where(p => p.AdminCore).Select(p => p.Code).ToList();

        foreach (var role in Roles.All.Where(r => r != Roles.Admin))
            Assert.Empty(SystemRoleDefaults.For(role).Intersect(core));
    }

    [Fact]
    public void TheDefaultsOfTheOtherFourRoles_AreExactlyThoseOfTheAdr()
    {
        Assert.Equal(
            new[] { Permissions.MasterDataRead, Permissions.DocumentApproveLarge, Permissions.StockRead, Permissions.ReportsRead, Permissions.AuditRead, Permissions.CostView, Permissions.ValuationView, Permissions.AccountingExport }.Order(StringComparer.Ordinal),
            SystemRoleDefaults.For(Roles.Owner).Order(StringComparer.Ordinal));
        Assert.Equal(
            new[]
            {
                Permissions.MasterDataManage, Permissions.MasterDataRead, Permissions.PartnersCreate, Permissions.DocumentCreate, Permissions.DocumentEditAny, Permissions.DocumentApprove,
                Permissions.DocumentPost, Permissions.DocumentPostApproved, Permissions.DocumentReverse, Permissions.StockNegativeOverride, Permissions.StockRead,
                Permissions.ReportsRead, Permissions.CostView, Permissions.ValuationView
            }.Order(StringComparer.Ordinal),
            SystemRoleDefaults.For(Roles.Supervisor).Order(StringComparer.Ordinal));
        Assert.Equal(
            new[] { Permissions.MasterDataRead, Permissions.DocumentCreate, Permissions.DocumentPost, Permissions.StockRead }.Order(StringComparer.Ordinal),
            SystemRoleDefaults.For(Roles.WarehouseStaff).Order(StringComparer.Ordinal));
        Assert.Equal(
            new[] { Permissions.MasterDataRead, Permissions.PartnersCreate, Permissions.DocumentCreate, Permissions.StockRead, Permissions.CostView }.Order(StringComparer.Ordinal),
            SystemRoleDefaults.For(Roles.Clerk).Order(StringComparer.Ordinal));
    }
}
