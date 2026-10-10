namespace Ims.Domain;

public class Warehouse : Entity
{
    private Warehouse() { }

    public string Code { get; private set; } = "";
    public string Name { get; private set; } = "";
    public string? Contact { get; private set; }
    public string? Notes { get; private set; }
    public bool IsActive { get; private set; } = true;

    public static Warehouse Create(string code, string name, string? contact = null, string? notes = null, bool isActive = true)
    {
        var w = new Warehouse();
        w.Update(code, name, contact, notes, isActive);
        return w;
    }

    public void Update(string code, string name, string? contact, string? notes, bool isActive)
    {
        if (string.IsNullOrWhiteSpace(code)) throw new DomainException(Messages.WarehouseCodeRequired);
        if (string.IsNullOrWhiteSpace(name)) throw new DomainException(Messages.WarehouseNameRequired);
        Code = code.Trim().ToUpperInvariant();
        Name = name.Trim();
        Contact = contact;
        Notes = notes;
        IsActive = isActive;
    }

    /// <summary>Documents can only be created in an active warehouse.</summary>
    public void EnsureUsable()
    {
        if (!IsActive) throw new DomainException(Messages.WarehouseInactive);
    }
}
