namespace Ims.Domain;

/// <summary>
/// A custom role was created. Carries what the audit row of a create prints as its after-state: the code, the name as stored (trimmed) and the permissions in catalog order (the order of the role editor, not the order given),
/// and the code of the role it was copied from, or null. Ids and facts only: never the role.
/// </summary>
public sealed record RoleCreated(Guid RoleId, string Code, string Name, IReadOnlyList<string> Permissions, string? CopiedFrom) : IDomainEvent;
