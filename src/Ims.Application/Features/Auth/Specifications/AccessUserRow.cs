namespace Ims.Application.Features.Auth;

/// <summary>What the access snapshot needs from the user's own row.</summary>
public sealed record AccessUserRow(int RoleId, bool AllWarehouses, IReadOnlyList<int> WarehouseIds);
