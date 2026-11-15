namespace Ims.Domain;

/// <summary>
/// A module was turned on or off for the installation. Carries what the audit row prints: the module's code, whether it was on before and is on now, the reason (trimmed) and, when it was turned on, how many
/// roles get the module's permissions back. A request for the state the module already has is no event.
/// </summary>
public sealed record ModuleSwitched(string ModuleCode, bool EnabledBefore, bool EnabledAfter, string Reason, int RolesRegainingGrants) : IDomainEvent;
