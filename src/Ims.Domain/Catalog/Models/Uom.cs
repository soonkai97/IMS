namespace Ims.Domain;

/// <summary>Unit of measure (piece, carton, kg ...). Codes are stored upper-case.</summary>
public class Uom : PublicEntity
{
    private Uom() { }

    public string Code { get; private set; } = "";
    public string Name { get; private set; } = "";

    public static Uom Create(string code, string? name = null)
    {
        var u = new Uom();
        u.Update(code, name);
        return u;
    }

    public void Update(string code, string? name)
    {
        if (string.IsNullOrWhiteSpace(code)) throw new DomainException(Messages.UomCodeRequired);
        Code = code.Trim().ToUpperInvariant();
        Name = string.IsNullOrWhiteSpace(name) ? Code : name.Trim();
    }
}
