namespace Ims.Domain;

/// <summary>The text and state of a role at one moment: what <see cref="RoleUpdated"/> carries as its before and after.</summary>
public sealed record RoleSummary(string Name, string Description, bool IsActive);
