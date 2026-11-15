namespace Ims.Domain;

/// <summary>What a user save changes, at one moment: what <see cref="UserUpdated"/> carries as its before and after. The warehouses are the internal ids the data scope keeps. No credential is part of it.</summary>
public sealed record UserSummary(string Username, string RoleCode, bool AllWarehouses, bool IsActive, IReadOnlyList<int> WarehouseIds);
