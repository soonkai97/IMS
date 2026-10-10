namespace Ims.Domain;

public class UserWarehouse : Entity
{
    private UserWarehouse() { }

    public int UserId { get; private set; }
    public int WarehouseId { get; private set; }

    public static UserWarehouse For(int warehouseId) => new() { WarehouseId = warehouseId };
}
