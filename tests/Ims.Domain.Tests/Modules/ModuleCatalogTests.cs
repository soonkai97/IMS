using System.Reflection;
using System.Text.RegularExpressions;

namespace Ims.Domain.Tests.Modules;

/// <summary>The module catalog is the source of truth for what an installation can switch on (ADR modules-and-tracking-dimensions 4.1): complete, unique, well formed, and safe as a setting key.</summary>
public partial class ModuleCatalogTests
{
    [GeneratedRegex(@"^[a-z]+$")]
    private static partial Regex CodePattern();

    private static IReadOnlyList<string> Constants { get; } = typeof(ModuleCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(f => f is { IsLiteral: true } && f.FieldType == typeof(string))
        .Select(f => (string)f.GetRawConstantValue()!).ToList();

    [Fact]
    public void TheCatalogListsThePurchasingSalesAndReturnsModules()
    {
        Assert.Equal([ModuleCodes.Purchasing, ModuleCodes.Sales, ModuleCodes.Returns], ModuleCatalog.Codes);
    }

    [Fact]
    public void TheSalesModuleIsNamedAsTheContractSays_AndRequiresNothing()
    {
        var sales = ModuleCatalog.Find(ModuleCodes.Sales)!;

        Assert.Equal("sales", sales.Code);
        Assert.Equal("Sales", sales.Name);
        Assert.Equal("Sales orders, delivery against them and price lists.", sales.Description);
        Assert.Empty(sales.Requires);
    }

    [Fact]
    public void EveryModuleCodeConstantIsInTheCatalogExactlyOnce_AndNothingElseIs()
    {
        Assert.Equal(Constants.Order(StringComparer.Ordinal), ModuleCatalog.Codes.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void TheCodesAreUnique()
    {
        Assert.Empty(ModuleCatalog.Codes.GroupBy(c => c, StringComparer.Ordinal).Where(g => g.Count() > 1).Select(g => g.Key));
    }

    [Fact]
    public void TheCodesAreLowerCaseWords_AtMostThirtyTwoCharacters_SoTheSettingKeyFits()
    {
        foreach (var code in ModuleCatalog.Codes)
        {
            Assert.Matches(CodePattern(), code);
            Assert.True(code.Length <= Limits.ModuleCodeMaxLength, code);
            Assert.True(SettingKeys.ForModule(code).Length <= Limits.SettingKeyMaxLength, code);
        }
    }

    [Fact]
    public void EveryEntryHasANameAndAOneSentenceDescription()
    {
        foreach (var module in ModuleCatalog.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(module.Name), module.Code);
            Assert.False(string.IsNullOrWhiteSpace(module.Description), module.Code);
            Assert.EndsWith(".", module.Description);
        }
    }

    [Fact]
    public void ReturnsIsTheOnlyModuleWithPrerequisites_SalesAndPurchasing_AndNothingRequiresItself()
    {
        Assert.Equal([ModuleCodes.Returns], ModuleCatalog.All.Where(m => m.Requires.Count > 0).Select(m => m.Code));
        Assert.Equal([ModuleCodes.Sales, ModuleCodes.Purchasing], ModuleCatalog.Find(ModuleCodes.Returns)!.Requires);
        Assert.Equal("Customer and supplier returns linked to the original document.", ModuleCatalog.Find(ModuleCodes.Returns)!.Description);
    }

    [Fact]
    public void MissingPrerequisites_NamesTheModulesThatAreOff_InTheOrderTheyAreRequired()
    {
        Assert.Equal([ModuleCodes.Sales, ModuleCodes.Purchasing], ModuleDependencies.MissingPrerequisites(ModuleCodes.Returns, new HashSet<string>()).Select(m => m.Code));
        Assert.Equal([ModuleCodes.Purchasing], ModuleDependencies.MissingPrerequisites(ModuleCodes.Returns, new HashSet<string> { ModuleCodes.Sales }).Select(m => m.Code));
        Assert.Empty(ModuleDependencies.MissingPrerequisites(ModuleCodes.Returns, new HashSet<string> { ModuleCodes.Sales, ModuleCodes.Purchasing }));
        Assert.Empty(ModuleDependencies.MissingPrerequisites(ModuleCodes.Sales, new HashSet<string>()));   // a module without a prerequisite is never refused
        Assert.Empty(ModuleDependencies.MissingPrerequisites("batch", new HashSet<string>()));            // nor a code the catalog does not know
    }

    [Fact]
    public void EnabledDependents_NamesTheModulesThatAreOnAndNeedIt()
    {
        var all = new HashSet<string> { ModuleCodes.Sales, ModuleCodes.Purchasing, ModuleCodes.Returns };

        Assert.Equal([ModuleCodes.Returns], ModuleDependencies.EnabledDependents(ModuleCodes.Sales, all).Select(m => m.Code));
        Assert.Equal([ModuleCodes.Returns], ModuleDependencies.EnabledDependents(ModuleCodes.Purchasing, all).Select(m => m.Code));
        Assert.Empty(ModuleDependencies.EnabledDependents(ModuleCodes.Sales, new HashSet<string> { ModuleCodes.Sales, ModuleCodes.Purchasing })); // returns is off
        Assert.Empty(ModuleDependencies.EnabledDependents(ModuleCodes.Returns, all));
    }

    [Fact]
    public void RequiresPointsToRealCodes_NeverToItself_AndHasNoCycle()
    {
        foreach (var module in ModuleCatalog.All)
        {
            Assert.All(module.Requires, required =>
            {
                Assert.True(ModuleCatalog.IsKnown(required), $"{module.Code} requires unknown {required}");
                Assert.NotEqual(module.Code, required);
            });
            Assert.False(ReachesItself(module.Code), $"{module.Code} requires itself through a chain");
        }
    }

    private static bool ReachesItself(string code)
    {
        var seen = new HashSet<string>();
        var pending = new Stack<string>(ModuleCatalog.Find(code)!.Requires);
        while (pending.Count > 0)
        {
            var next = pending.Pop();
            if (next == code) return true;
            if (seen.Add(next)) foreach (var required in ModuleCatalog.Find(next)!.Requires) pending.Push(required);
        }
        return false;
    }

    [Fact]
    public void FindAndIsKnown_AgreeWithTheCatalog_AndAnUnregisteredCodeIsNotKnown()
    {
        Assert.Equal(ModuleCodes.Purchasing, ModuleCatalog.Find(ModuleCodes.Purchasing)!.Code);
        Assert.Null(ModuleCatalog.Find("batch"));
        Assert.False(ModuleCatalog.IsKnown("accounting")); // removed in 2026-10 (RemoveAccountingModule): never on, whatever a stored row says
        Assert.False(ModuleCatalog.IsKnown("Purchasing")); // codes are exact: the same rule as for permission codes
        Assert.False(ModuleCatalog.IsKnown(""));
    }

    [Fact]
    public void AModuleSwitchIsTheSettingRowModuleDotCode()
    {
        Assert.Equal("Module.purchasing", SettingKeys.ForModule(ModuleCodes.Purchasing));
        Assert.StartsWith(SettingKeys.ModulePrefix, SettingKeys.ForModule(ModuleCodes.Purchasing));
    }
}
