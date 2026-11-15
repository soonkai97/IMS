namespace Ims.Domain;

/// <summary>The permissions of a role were replaced and the set is not the same: the codes added and the codes removed, each in catalog order. Not raised for the same set again.</summary>
public sealed record RolePermissionsChanged(Guid RoleId, string Code, IReadOnlyList<string> Added, IReadOnlyList<string> Removed) : IDomainEvent;
