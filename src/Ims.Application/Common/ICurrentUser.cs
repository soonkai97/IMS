using Ims.Domain;
namespace Ims.Application;

/// <summary>The authenticated caller. Implemented in the Api layer from the HTTP context.</summary>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }
    int UserId { get; }
    string Username { get; }

    /// <summary>The code of the caller's role, from the token. For display, audit and the checks of RP-4b (approval limit, warehouse scope); a permission is never decided by it.</summary>
    string Role { get; }
    IReadOnlyCollection<int> WarehouseIds { get; }
    bool CanAccessWarehouse(int warehouseId);

    /// <summary>The token says the user sees every warehouse.</summary>
    bool AllWarehouses { get; }

    /// <summary>
    /// What the caller may do: the <c>perm</c> claims of the presented access token, the one input of every permission check (an unauthenticated caller holds
    /// nothing). Codes the catalog does not know grant nothing.
    /// </summary>
    ActorRights Rights();
}
