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
    public void TheCatalogListsTheAccountingModule_AndNoOtherYet()
    {
        Assert.Equal([ModuleCodes.Accounting], ModuleCatalog.Codes);
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
    public void NoModuleRequiresAnotherYet_SoTheSwitchDoesNotNeedToEnforceIt()
    {
        Assert.All(ModuleCatalog.All, m => Assert.True(m.Requires.Count == 0, $"{m.Code}: enforce Requires in SwitchModuleHandler first."));
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
        Assert.Equal(ModuleCodes.Accounting, ModuleCatalog.Find(ModuleCodes.Accounting)!.Code);
        Assert.Null(ModuleCatalog.Find("batch"));
        Assert.False(ModuleCatalog.IsKnown("Accounting")); // codes are exact: the same rule as for permission codes
        Assert.False(ModuleCatalog.IsKnown(""));
    }

    [Fact]
    public void AModuleSwitchIsTheSettingRowModuleDotCode()
    {
        Assert.Equal("Module.accounting", SettingKeys.ForModule(ModuleCodes.Accounting));
        Assert.StartsWith(SettingKeys.ModulePrefix, SettingKeys.ForModule(ModuleCodes.Accounting));
    }
}
