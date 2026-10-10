namespace Ims.Api.Tests.Support;

/// <summary>One page of the audit log as returned by <c>GET /api/audit</c> (the domain type has no public setters, so tests read this shape).</summary>
public sealed record AuditPage(IReadOnlyList<AuditEntry> Items, int Total, int Page, int PageSize);
