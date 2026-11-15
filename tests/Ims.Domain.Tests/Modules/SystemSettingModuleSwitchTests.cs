namespace Ims.Domain.Tests.Modules;

/// <summary>
/// DDD-14: <see cref="SystemSetting"/> is a root without a public id, so it implements <see cref="IAggregateRoot"/> itself, and the row of a module raises <see cref="ModuleSwitched"/> when the module is switched
/// (docs/architecture/ddd-roadmap.md 2.2, 2.4). The event carries what the audit row prints. A request that changes nothing, a refused change and every other setting raise nothing.
/// </summary>
public class SystemSettingModuleSwitchTests
{
    private static Reason Why(string text = "Go live on Monday") => Reason.Of(text, Messages.ModuleReasonRequired);

    [Fact]
    public void TheFirstTurnOn_CreatesTheRowOfTheModule_AndRaisesOneEventFromOffToOn()
    {
        var setting = SystemSetting.CreateModuleSwitch(ModuleCodes.Purchasing, enabled: true, Why("  Go live on Monday  "), rolesRegainingGrants: 2);

        Assert.Equal(("Module.purchasing", "true"), (setting.Key, setting.Value));
        var raised = Assert.IsType<ModuleSwitched>(Assert.Single(setting.DomainEvents));
        Assert.Equal(new ModuleSwitched(ModuleCodes.Purchasing, EnabledBefore: false, EnabledAfter: true, "Go live on Monday", RolesRegainingGrants: 2), raised);
    }

    [Theory]
    [InlineData("false", true, false)]   // an off row turned on
    [InlineData("TRUE", true, false)]    // a value that is not exactly "true" is off
    [InlineData("", true, false)]
    [InlineData("true", false, true)]    // an on row turned off
    public void SwitchingAModuleRow_ChangesTheValue_AndRaisesTheEventWithTheStateItHadBeforeAndTheStateItHasNow(string stored, bool enabled, bool before)
    {
        var setting = SystemSetting.Create(SettingKeys.ForModule(ModuleCodes.Sales), stored);

        setting.SwitchModule(ModuleCodes.Sales, enabled, Why("Not selling this quarter"), rolesRegainingGrants: enabled ? 3 : 0);

        Assert.Equal(SettingValues.From(enabled), setting.Value);
        var raised = Assert.IsType<ModuleSwitched>(Assert.Single(setting.DomainEvents));
        Assert.Equal(new ModuleSwitched(ModuleCodes.Sales, before, enabled, "Not selling this quarter", enabled ? 3 : 0), raised);
    }

    [Fact]
    public void AskingForTheStateTheRowHas_ChangesNothingAndRaisesNothing()
    {
        var on = SystemSetting.Create(SettingKeys.ForModule(ModuleCodes.Sales), SettingValues.True);
        var off = SystemSetting.Create(SettingKeys.ForModule(ModuleCodes.Sales), SettingValues.False);

        on.SwitchModule(ModuleCodes.Sales, enabled: true, Why(), rolesRegainingGrants: 1);
        off.SwitchModule(ModuleCodes.Sales, enabled: false, Why(), rolesRegainingGrants: 0);

        Assert.Equal((SettingValues.True, SettingValues.False), (on.Value, off.Value));
        Assert.Empty(on.DomainEvents);
        Assert.Empty(off.DomainEvents);
        Assert.Empty(on.RaisedEvents);
    }

    [Fact]
    public void OnlyTheRowOfAModuleCanBeSwitched_AnyOtherSettingRefusesAndRaisesNothing()
    {
        var setting = SystemSetting.Create(SettingKeys.AllowNegativeStock, SettingValues.False);

        Assert.Throws<InvalidOperationException>(() => setting.SwitchModule(ModuleCodes.Sales, enabled: true, Why(), rolesRegainingGrants: 0));

        Assert.Equal(SettingValues.False, setting.Value);
        Assert.Empty(setting.DomainEvents);
    }

    [Theory]
    [InlineData("module.sales")]   // a row edited by hand: the database finds it by key without regard to case
    [InlineData("Module.SALES")]
    [InlineData("MODULE.Sales")]
    public void ARowWhoseKeyDiffersOnlyInCase_IsSwitchedWithoutError_AndTheEventNamesTheModuleByItsCatalogCode(string storedKey)
    {
        var setting = SystemSetting.Create(storedKey, SettingValues.False);

        setting.SwitchModule(ModuleCodes.Sales, enabled: true, Why(), rolesRegainingGrants: 1);

        Assert.Equal(SettingValues.True, setting.Value);
        var raised = Assert.IsType<ModuleSwitched>(Assert.Single(setting.DomainEvents));
        Assert.Equal(new ModuleSwitched(ModuleCodes.Sales, EnabledBefore: false, EnabledAfter: true, "Go live on Monday", RolesRegainingGrants: 1), raised);
    }

    [Fact]
    public void ARowOfAnotherModule_IsRefused()
    {
        var setting = SystemSetting.Create(SettingKeys.ForModule(ModuleCodes.Purchasing), SettingValues.False);

        Assert.Throws<InvalidOperationException>(() => setting.SwitchModule(ModuleCodes.Sales, enabled: true, Why(), rolesRegainingGrants: 0));

        Assert.Empty(setting.DomainEvents);
    }

    [Fact]
    public void ChangingAPlainSetting_RaisesNothing_AndNeitherDoesCreatingOne()
    {
        var setting = SystemSetting.Create(SettingKeys.AllowNegativeStock, SettingValues.False);
        var module = SystemSetting.Create(SettingKeys.ForModule(ModuleCodes.Sales), SettingValues.False);

        setting.ChangeValue(SettingValues.True);
        module.ChangeValue(SettingValues.True);   // the settings screen and the seeder write values without a switch: no event, as before

        Assert.Empty(setting.DomainEvents);
        Assert.Empty(module.DomainEvents);
    }

    [Fact]
    public void TheRaisedEventsAreNumbered_InTheOrderTheyWereRaised_AndAreForgottenOnClear()
    {
        var a = SystemSetting.Create(SettingKeys.ForModule(ModuleCodes.Sales), SettingValues.False);
        var b = SystemSetting.Create(SettingKeys.ForModule(ModuleCodes.Purchasing), SettingValues.False);

        a.SwitchModule(ModuleCodes.Sales, enabled: true, Why(), rolesRegainingGrants: 0);
        b.SwitchModule(ModuleCodes.Purchasing, enabled: true, Why(), rolesRegainingGrants: 0);
        a.SwitchModule(ModuleCodes.Sales, enabled: false, Why(), rolesRegainingGrants: 0);

        Assert.Equal(2, a.RaisedEvents.Count);
        Assert.True(a.RaisedEvents[0].Sequence < b.RaisedEvents[0].Sequence);
        Assert.True(b.RaisedEvents[0].Sequence < a.RaisedEvents[1].Sequence);
        Assert.Equal(a.DomainEvents, a.RaisedEvents.Select(e => e.Event));

        a.ClearDomainEvents();

        Assert.Empty(a.DomainEvents);
        Assert.Empty(a.RaisedEvents);
        Assert.Single(b.DomainEvents);
    }
}
