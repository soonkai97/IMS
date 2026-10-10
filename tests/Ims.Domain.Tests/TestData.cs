using System.Reflection;

namespace Ims.Domain.Tests;

/// <summary>
/// Builders for domain objects. Entities have private setters and factories, so state is set up through public behaviour;
/// reflection is used only to assign database-generated ids (and the few fields no public method can reach).
/// </summary>
public static class TestData
{
    /// <summary>A fixed instant (UTC) so no test depends on the real clock.</summary>
    public static readonly DateTime Now = new(2026, 1, 15, 8, 0, 0, DateTimeKind.Utc);

    public const int Creator = 10;
    public const int Submitter = 11;
    public const int Approver = 12;

    public const int PcsId = 1;
    public const int CartonId = 2;

    /// <summary>Assigns a database-generated id (private/protected setter) and returns the same instance.</summary>
    public static T WithId<T>(this T entity, long id) where T : notnull
    {
        var property = FindProperty(entity.GetType(), "Id");
        property.SetValue(entity, Convert.ChangeType(id, property.PropertyType));
        return entity;
    }

    /// <summary>Sets a property that has no public setter. Use only where no public behaviour reaches the state.</summary>
    public static T WithPrivate<T>(this T entity, string name, object? value) where T : notnull
    {
        FindProperty(entity.GetType(), name).SetValue(entity, value);
        return entity;
    }

    private static PropertyInfo FindProperty(Type type, string name)
    {
        for (var t = type; t is not null; t = t.BaseType)
            if (t.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly) is { } p)
                return p;
        throw new InvalidOperationException($"{type.Name} has no property {name}");
    }

    public static Uom NewUom(int id = PcsId, string code = "PCS") => Uom.Create(code).WithId(id);

    public static Warehouse NewWarehouse(int id = 1, string code = "WH1", bool active = true) =>
        Warehouse.Create(code, "Main warehouse", isActive: active).WithId(id);

    public static Partner NewSupplier(int id = 100, bool active = true) => Partner.Create(PartnerType.Supplier, "Acme Supply", isActive: active).WithId(id);

    public static Partner NewCustomer(int id = 200, bool active = true) => Partner.Create(PartnerType.Customer, "Beta Retail", isActive: active).WithId(id);

    /// <summary>A product whose base unit is PCS (id 1). Pass <paramref name="cartonFactor"/> to add a CARTON (id 2) conversion.</summary>
    public static Product NewProduct(int id = 1, string sku = "SKU-1", bool active = true, decimal? cartonFactor = null)
    {
        var product = Product.Create(sku, $"Product {sku}", null, NewUom(), 0m, active).WithId(id);
        if (cartonFactor is { } factor)
            product.SetConversions([new ConversionDefinition(CartonId, PcsId, factor)]);
        return product;
    }

    public static StockDocumentLine NewLine(Product product, DocumentType type, decimal qty, decimal? unitCost = null, int? uomId = null, int lineId = 0)
    {
        var line = StockDocumentLine.Create(product, type, qty, uomId, unitCost, null);
        return lineId == 0 ? line : line.WithId(lineId);
    }

    /// <summary>A Draft receipt with the given (product, qty, cost) lines. Lines get ids 1..n in the order given.</summary>
    public static StockDocument NewReceipt(int id = 1, params (Product Product, decimal Qty, decimal Cost)[] lines)
    {
        var doc = StockDocument.Create(DocumentType.Receipt, NewWarehouse(), NewSupplier(), null, null, Creator, Now).WithId(id);
        doc.AddLines(lines.Select((l, i) => NewLine(l.Product, DocumentType.Receipt, l.Qty, l.Cost, lineId: i + 1)));
        return doc;
    }

    /// <summary>A Draft issue with the given (product, qty) lines. Lines get ids 1..n in the order given.</summary>
    public static StockDocument NewIssue(int id = 2, params (Product Product, decimal Qty)[] lines)
    {
        var doc = StockDocument.Create(DocumentType.Issue, NewWarehouse(), NewCustomer(), IssuePurpose.Sale, null, Creator, Now).WithId(id);
        doc.AddLines(lines.Select((l, i) => NewLine(l.Product, DocumentType.Issue, l.Qty, lineId: i + 1)));
        return doc;
    }

    public static StockDocument NewSubmittedReceipt(bool requiresApproval, int id = 1)
    {
        var doc = NewReceipt(id, (NewProduct(), 5m, 2m));
        doc.Submit(Submitter, requiresApproval, Now);
        return doc;
    }

    public static StockDocument NewApprovedReceipt()
    {
        var doc = NewSubmittedReceipt(requiresApproval: true);
        doc.Approve(Approver, allowSelfApproval: false, Now);
        return doc;
    }

    public static StockDocument NewPostedReceipt()
    {
        var doc = NewSubmittedReceipt(requiresApproval: false);
        doc.MarkPosted(Approver, Now);
        return doc;
    }

    /// <summary>A balance holding <paramref name="qty"/> at <paramref name="avgCost"/> (built by receiving).</summary>
    public static StockBalance NewBalance(int productId, decimal qty = 0m, decimal avgCost = 0m, int warehouseId = 1)
    {
        var balance = StockBalance.OpenFor(productId, warehouseId);
        if (qty > 0) balance.Receive(qty, avgCost);
        return balance;
    }

    public static SystemSettings Settings(bool allowNegative = false, bool allowSelfApproval = false) =>
        new(allowNegative, allowSelfApproval, Limits.DefaultLowStockCooldownHours);

    public static PostingContext NewContext(StockDocument doc, bool allowNegative = false, bool canOverride = false) =>
        new(doc, Settings(allowNegative), Approver, canOverride, Now);

    public static PostingEngine NewEngine() => new([new ReceiptPostingStrategy(), new IssuePostingStrategy()]);

    public static Dictionary<int, StockBalance> Balances(params StockBalance[] balances) =>
        balances.ToDictionary(b => b.ProductId);

    /// <summary>Submits an issue/receipt with no approval need so that it can be posted.</summary>
    public static StockDocument Submitted(this StockDocument doc, bool requiresApproval = false)
    {
        doc.Submit(Submitter, requiresApproval, Now);
        return doc;
    }

    /// <summary>Submits the document as an override (stock is short) and has the approver approve it with the given reason.</summary>
    public static StockDocument ApprovedWithOverride(this StockDocument doc, string? reason = "customer collecting today")
    {
        doc.Submit(Submitter, requiresApproval: true, Now, requiresOverride: true);
        doc.Approve(Approver, allowSelfApproval: false, Now, reason);
        return doc;
    }
}
