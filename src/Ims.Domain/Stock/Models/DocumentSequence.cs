namespace Ims.Domain;

/// <summary>BR-11: running number per document type, warehouse and month. Concurrent allocation is guarded by the row version.</summary>
public class DocumentSequence : AuditableEntity
{
    private DocumentSequence() { }

    public string Prefix { get; private set; } = "";
    public int YearMonth { get; private set; }
    public int WarehouseId { get; private set; }
    public int LastNumber { get; private set; }

    public static DocumentSequence Start(string prefix, int yearMonth, int warehouseId) =>
        new() { Prefix = prefix, YearMonth = yearMonth, WarehouseId = warehouseId };

    public int Next() => ++LastNumber;
}
