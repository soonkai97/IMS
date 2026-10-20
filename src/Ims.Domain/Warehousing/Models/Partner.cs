namespace Ims.Domain;

/// <summary>A supplier (receipts) or customer (issues).</summary>
public class Partner : PublicEntity
{
    private Partner() { }

    public PartnerType Type { get; private set; }
    public string Name { get; private set; } = "";
    public string? Contact { get; private set; }
    public bool IsActive { get; private set; } = true;
    /// <summary>Tax-ready fields of the accounting module (shape only, see <see cref="TaxFieldShapes"/>); both stay when the module is switched off.</summary>
    public string? Tin { get; private set; }
    public string? SstRegNo { get; private set; }

    public static Partner Create(PartnerType type, string name, string? contact = null, bool isActive = true)
    {
        var p = new Partner();
        p.Update(type, name, contact, isActive);
        return p;
    }

    public void Update(PartnerType type, string name, string? contact, bool isActive)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new DomainException(Messages.PartnerNameRequired);
        Type = type;
        Name = name.Trim();
        Contact = contact;
        IsActive = isActive;
    }

    /// <summary>Replaces both tax-ready fields (blank clears one); refused when a value has the wrong shape. Not part of <see cref="Update"/>, so a save that does not carry them leaves them alone.</summary>
    public void SetTaxIdentifiers(string? tin, string? sstRegNo)
    {
        var t = TaxFieldShapes.Accept(tin, TaxFieldShapes.TinError);
        var reg = TaxFieldShapes.Accept(sstRegNo, TaxFieldShapes.SstRegNoError);
        Tin = t;
        SstRegNo = reg;
    }

    /// <summary>Receipts need a supplier, issues need a customer, and the partner must still be active.</summary>
    public void EnsureUsableFor(DocumentType documentType)
    {
        if (!IsActive) throw new DomainException(Messages.PartnerInactive);
        var expected = documentType == DocumentType.Receipt ? PartnerType.Supplier : PartnerType.Customer;
        if (Type != expected) throw new DomainException(Messages.PartnerWrongType(documentType, expected));
    }
}
