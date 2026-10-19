namespace Ims.Application.Features.Auth;

/// <summary>
/// The signed-in user. <c>Permissions</c> lists the codes of the access token (the role's grants when it was issued) in catalog order; the web reads it for menus and
/// buttons, the API decides by the token itself. <c>Modules</c> lists the codes of the modules this installation has on (read when the answer is built, not carried by the token), in catalog order.
/// </summary>
public record UserInfo(Guid Id, string Username, string DisplayName, string Role, string RoleName, IReadOnlyList<string> Permissions, bool SeesAllWarehouses, IReadOnlyList<Guid> WarehouseIds, IReadOnlyList<string> Modules);
