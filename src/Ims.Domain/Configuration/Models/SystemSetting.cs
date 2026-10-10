namespace Ims.Domain;

public class SystemSetting : AuditableEntity
{
    private SystemSetting() { }

    public string Key { get; private set; } = "";
    public string Value { get; private set; } = "";

    public static SystemSetting Create(string key, string value) => new() { Key = key, Value = value };

    public void ChangeValue(string value) => Value = value;
}
