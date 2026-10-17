namespace Ims.Application.Features.Auth;

/// <summary>
/// The signed-in user. <c>RoleName</c> and <c>SeesAllWarehouses</c> were added for the roles work (additive). <c>Permissions</c> is still the list of the
/// user's system role from the fixed map, until the permissions come from the database.
/// </summary>
public record UserInfo(Guid Id, string Username, string DisplayName, string Role, string RoleName, IReadOnlyList<string> Permissions, bool SeesAllWarehouses, IReadOnlyList<Guid> WarehouseIds);
