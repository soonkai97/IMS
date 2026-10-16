using Ims.Application;

namespace Ims.Api.Tests.Support;

/// <summary>An authenticated caller for a <c>DbContext</c> built outside the API host, so the audit rows it writes name an actor.</summary>
public sealed class FixedUser(int userId, string username) : ICurrentUser
{
    public bool IsAuthenticated => true;
    public int UserId { get; } = userId;
    public string Username { get; } = username;
    public string Role => Domain.Roles.Admin;
    public IReadOnlyCollection<int> WarehouseIds => [];
    public bool CanAccessWarehouse(int warehouseId) => true;
}
