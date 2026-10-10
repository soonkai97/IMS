namespace Ims.Domain;

/// <summary>
/// A stocked item. The aggregate owns its barcodes and unit conversions and guards its own invariants:
/// every change goes through a method, so an invalid product cannot exist.
/// </summary>
public class Product : Entity
{
    private Product() { }

    public string Sku { get; private set; } = "";
    public string Name { get; private set; } = "";
    public int? CategoryId { get; private set; }
    public int BaseUomId { get; private set; }
    public decimal SafetyStock { get; private set; }
    public bool IsActive { get; private set; } = true;
    public Category? Category { get; private set; }
    public Uom BaseUom { get; private set; } = null!;
    private readonly List<ProductBarcode> _barcodes = [];
    private readonly List<UomConversion> _conversions = [];

    /// <summary>Read-only view: change barcodes through <see cref="SetBarcodes"/> so they stay trimmed and unique.</summary>
    public IReadOnlyList<ProductBarcode> Barcodes => _barcodes;

    /// <summary>Read-only view: change conversions through <see cref="SetConversions"/>.</summary>
    public IReadOnlyList<UomConversion> Conversions => _conversions;

    public static Product Create(string sku, string name, Category? category, Uom baseUom, decimal safetyStock, bool isActive = true)
    {
        var p = new Product();
        p.Update(sku, name, category, safetyStock, isActive);
        p.AssignBaseUnit(baseUom);
        return p;
    }

    public void Update(string sku, string name, Category? category, decimal safetyStock, bool isActive)
    {
        if (string.IsNullOrWhiteSpace(sku)) throw new DomainException(Messages.SkuRequired);
        if (string.IsNullOrWhiteSpace(name)) throw new DomainException(Messages.ProductNameRequired);
        if (safetyStock < 0) throw new DomainException(Messages.SafetyStockNegative);

        Sku = sku.Trim();
        Name = name.Trim();
        Category = category;
        CategoryId = category?.Id;
        SafetyStock = safetyStock;
        IsActive = isActive;
    }

    /// <summary>BR-04: refused once stock movements exist.</summary>
    public void ChangeBaseUnit(Uom newUnit, bool hasStockMovements)
    {
        BusinessRules.Ensure(new BaseUnitLockedRule(BaseUomId, newUnit.Id, hasStockMovements));
        if (BaseUomId != newUnit.Id) _conversions.Clear(); // existing conversions lead into the old base unit and no longer apply
        AssignBaseUnit(newUnit);
    }

    /// <summary>Makes the product's barcodes exactly <paramref name="codes"/>: removes missing ones, adds new ones.</summary>
    public void SetBarcodes(IEnumerable<string> codes)
    {
        var wanted = codes.Select(c => c.Trim()).Where(c => c.Length > 0).ToHashSet();
        _barcodes.RemoveAll(b => !wanted.Contains(b.Barcode));
        var existing = _barcodes.Select(b => b.Barcode).ToHashSet();
        foreach (var code in wanted.Where(c => !existing.Contains(c)))
            _barcodes.Add(ProductBarcode.Create(code));
    }

    /// <summary>Makes the product's unit conversions exactly <paramref name="definitions"/>. Every conversion must lead into the base unit.</summary>
    public void SetConversions(IEnumerable<ConversionDefinition> definitions)
    {
        var wanted = definitions.ToList();
        var duplicated = wanted.GroupBy(d => d.FromUomId).Any(g => g.Count() > 1);
        if (duplicated || wanted.Any(d => d.Factor <= 0 || d.ToUomId != BaseUomId || d.FromUomId == d.ToUomId))
            throw new DomainException(Messages.ConversionInvalid);

        _conversions.RemoveAll(c => !wanted.Any(d => d.FromUomId == c.FromUomId && d.ToUomId == c.ToUomId));
        foreach (var d in wanted)
        {
            var existing = _conversions.FirstOrDefault(c => c.FromUomId == d.FromUomId && c.ToUomId == d.ToUomId);
            if (existing is null) _conversions.Add(UomConversion.Create(d.FromUomId, d.ToUomId, d.Factor));
            else existing.ChangeFactor(d.Factor);
        }
    }

    /// <summary>Converts a quantity entered in <paramref name="uomId"/> to the base unit.</summary>
    public decimal ToBaseQuantity(decimal qty, int uomId) => UnitConversion.ToBase(qty, uomId, BaseUomId, Conversions);

    /// <summary>Deactivated products keep their history but cannot be used on new documents.</summary>
    public void EnsureUsableOnDocuments()
    {
        if (!IsActive) throw new DomainException(Messages.ProductInactive(Sku));
    }

    private void AssignBaseUnit(Uom unit)
    {
        BaseUom = unit;
        BaseUomId = unit.Id;
    }
}
