namespace Ims.Application;

/// <summary>
/// On a string property that holds JSON written by the application (the audit log's before/after): the keys that name a cost or value
/// (<see cref="Ims.Domain.VisibleWithAttribute"/> on the entity property or on <see cref="Ims.Domain.AuditFields"/>) are removed for a caller
/// who lacks the permission.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class ScrubsRestrictedKeysAttribute : Attribute;
