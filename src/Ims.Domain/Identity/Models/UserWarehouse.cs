namespace Ims.Domain;

public class UserWarehouse : Entity
{
    private UserWarehouse() { }

    public int UserId { get; private set; }
    public int WarehouseId { get; private set; }

    /// <summary>Navigation property used by read-side projections (the warehouse's public id).</summary>
    public Warehouse Warehouse { get; private set; } = null!;

    public static UserWarehouse For(int warehouseId) => new() { WarehouseId = warehouseId };
}
