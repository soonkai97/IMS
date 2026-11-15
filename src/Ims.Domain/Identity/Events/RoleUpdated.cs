namespace Ims.Domain;

/// <summary>A role was renamed, given another description, or activated or deactivated. Raised on every valid update, also when nothing changed, because the audit row of an update was always written. Carries the text and state before and after.</summary>
public sealed record RoleUpdated(Guid RoleId, string Code, RoleSummary Before, RoleSummary After) : IDomainEvent;
