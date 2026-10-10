namespace Ims.Domain;

public class InsufficientStockException : DomainException
{
    public InsufficientStockException(int productId, int warehouseId, decimal available, decimal requested)
        : base(Messages.InsufficientStock(productId, warehouseId, available, requested))
    {
        ProductId = productId;
        WarehouseId = warehouseId;
        Available = available;
        Requested = requested;
    }

    /// <summary>The same conflict described with the product's SKU, the warehouse's name and the unit, instead of ids.</summary>
    public InsufficientStockException(InsufficientStockException original, string sku, string warehouseName, string uomCode)
        : base(Messages.InsufficientStockNamed(sku, warehouseName, uomCode, original.Available, original.Requested))
    {
        ProductId = original.ProductId;
        WarehouseId = original.WarehouseId;
        Available = original.Available;
        Requested = original.Requested;
        Sku = sku;
    }

    public int ProductId { get; }
    public int WarehouseId { get; }
    public decimal Available { get; }
    public decimal Requested { get; }
    /// <summary>Null until the message has been put into words (see the application's shortage naming step).</summary>
    public string? Sku { get; }
}
