namespace Ims.Application;

/// <summary>The authenticated caller. Implemented in the Api layer from the HTTP context.</summary>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }
    int UserId { get; }
    string Username { get; }
    string Role { get; }
    IReadOnlyCollection<int> WarehouseIds { get; }
    bool CanAccessWarehouse(int warehouseId);
}
