namespace Ims.Domain;

public class InsufficientStockException(int productId, int warehouseId, decimal available, decimal requested)
    : DomainException(Messages.InsufficientStock(productId, warehouseId, available, requested))
{
    public int ProductId { get; } = productId;
    public int WarehouseId { get; } = warehouseId;
    public decimal Available { get; } = available;
    public decimal Requested { get; } = requested;
}
