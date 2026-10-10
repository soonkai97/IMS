namespace Ims.Application.Features.Audit;

/// <summary>What the audit log filters can be offered as dropdowns: the entity names and action names an audit row can carry.</summary>
public record AuditFilterOptions(IReadOnlyList<string> EntityTypes, IReadOnlyList<string> Actions);
