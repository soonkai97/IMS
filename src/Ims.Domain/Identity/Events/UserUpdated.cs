namespace Ims.Domain;

/// <summary>
/// A user was saved: username, role, scope, state or warehouses may have changed, or a new password was set (the password is not part of the event, nor is its hash). Raised on every valid save, also when nothing changed,
/// because the audit row of a user save was always written. Carries the state before and after.
/// </summary>
public sealed record UserUpdated(Guid UserId, UserSummary Before, UserSummary After) : IDomainEvent;
