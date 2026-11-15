namespace Ims.Domain;

/// <summary>A custom role was deleted (soft). Carries what the audit row of a delete prints as its before-state: the code, the name and the permissions it held, in catalog order.</summary>
public sealed record RoleDeleted(Guid RoleId, string Code, string Name, IReadOnlyList<string> Permissions) : IDomainEvent;
