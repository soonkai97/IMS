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

    /// <summary>The permission codes the presented access token carries (what its holder was granted when it was issued). Endpoint checks still go by <see cref="Role"/> until RP-4a.</summary>
    IReadOnlyCollection<string> PermissionCodes { get; }

    /// <summary>The token says the user sees every warehouse. Enforcement still goes by <see cref="Role"/> until RP-4a.</summary>
    bool AllWarehouses { get; }
}
