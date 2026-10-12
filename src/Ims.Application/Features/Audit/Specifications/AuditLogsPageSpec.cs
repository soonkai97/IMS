using Ardalis.Specification;
using Ims.Domain;
namespace Ims.Application.Features.Audit;

/// <summary>One page of the audit log, newest first, projected to the API contract (the entity never leaves the application layer).</summary>
public sealed class AuditLogsPageSpec : Specification<AuditLog, AuditLogDto>
{
    public AuditLogsPageSpec(AuditQueryFilter q)
    {
        AuditLogFilters.Apply(Query, q);
        var (page, size) = Paging.Normalize(q.Page, q.PageSize);
        Query.OrderByDescending(a => a.Id);
        if (CursorToken.Decode(q.Cursor) is { } after) Query.Where(a => a.Id < after).Take(size);
        else Query.Skip((page - 1) * size).Take(size);
        Query.Select(a => new AuditLogDto(a.Id, a.Actor, a.Action, a.EntityType, a.EntityId, a.Before, a.After, a.At));
    }
}
