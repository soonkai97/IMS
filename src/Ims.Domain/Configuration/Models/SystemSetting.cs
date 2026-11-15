namespace Ims.Domain;

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

    public static SystemSetting CreateModuleSwitch(string moduleCode, bool enabled, Reason reason, int rolesRegainingGrants) => Create(SettingKeys.ForModule(moduleCode), SettingValues.From(enabled));

    public void SwitchModule(bool enabled, Reason reason, int rolesRegainingGrants) => Value = SettingValues.From(enabled);

    public void ChangeValue(string value) => Value = value;
}
