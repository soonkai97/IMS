namespace Ims.Domain;

/// <summary>
/// One row of the key/value settings table. It is a root without a public id, so it implements <see cref="IAggregateRoot"/> itself (docs/architecture/ddd-roadmap.md 2.1, 2.2): the row of a module (<c>Module.&lt;code&gt;</c>)
/// raises <see cref="ModuleSwitched"/> when the module is switched, and no other write raises anything. The lists are get-only, so the persistence layer does not map them (the pattern of <see cref="AggregateRoot"/>).
/// </summary>
public sealed class SystemSetting : AuditableEntity, IAggregateRoot
{
    private readonly List<IDomainEvent> _events = [];
    private readonly List<RaisedEvent> _raised = [];

    private SystemSetting() { }

    public string Key { get; private set; } = "";
    public string Value { get; private set; } = "";

    public IReadOnlyList<IDomainEvent> DomainEvents => _events;

    public IReadOnlyList<RaisedEvent> RaisedEvents => _raised;

    public void ClearDomainEvents()
    {
        _events.Clear();
        _raised.Clear();
    }

    public static SystemSetting Create(string key, string value) => new() { Key = key, Value = value };

    /// <summary>The row of a module that has none yet, written by its first switch (a missing row means off, so the module was off before). Raises <see cref="ModuleSwitched"/>.</summary>
    public static SystemSetting CreateModuleSwitch(string moduleCode, bool enabled, Reason reason, int rolesRegainingGrants)
    {
        var setting = Create(SettingKeys.ForModule(moduleCode), SettingValues.From(enabled));
        setting.Raise(new ModuleSwitched(moduleCode, EnabledBefore: false, enabled, reason.Text, rolesRegainingGrants));
        return setting;
    }

    /// <summary>
    /// Turns the module of this row on or off and raises <see cref="ModuleSwitched"/> with the state it had (a value that is not exactly "true" is off). The state it already has changes nothing and raises nothing.
    /// </summary>
    /// <exception cref="InvalidOperationException">The row is not the switch of a module: no code path does that.</exception>
    public void SwitchModule(string moduleCode, bool enabled, Reason reason, int rolesRegainingGrants)
    {
        if (!Key.StartsWith(SettingKeys.ModulePrefix, StringComparison.Ordinal)) throw new InvalidOperationException($"The setting {Key} is not the switch of a module.");
        var before = SettingValues.IsTrue(Value);
        if (before == enabled) return;

        Value = SettingValues.From(enabled);
        Raise(new ModuleSwitched(Key[SettingKeys.ModulePrefix.Length..], before, enabled, reason.Text, rolesRegainingGrants));
    }

    public void ChangeValue(string value) => Value = value;

    /// <summary>Records a fact about a change that has just been made. Only the root itself can call it; the event gets the next number of the process-wide sequence.</summary>
    private void Raise(IDomainEvent domainEvent)
    {
        _events.Add(domainEvent);
        _raised.Add(RaisedEvent.Stamp(domainEvent));
    }
}
