namespace Ims.Domain;

/// <summary>
/// A user was created and written. Carries what the audit row of a create prints as its after-state: the username as stored (trimmed), the code of the role and whether the user sees every warehouse. Never the password
/// or its hash, and not the email or the display name (the row-level audit leaves them out too).
/// </summary>
public sealed record UserCreated(Guid UserId, string Username, string RoleCode, bool AllWarehouses) : IDomainEvent;
