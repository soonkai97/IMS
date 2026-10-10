using Ardalis.Specification;
using Ims.Domain;
namespace Ims.Application.Features.Audit;

internal static class AuditLogFilters
{
    public static void Apply(ISpecificationBuilder<AuditLog> query, AuditQueryFilter q)
    {
        query.AsNoTracking();
        // An entity name fragment becomes a short IN list of known names (seek on IX_AuditLog_EntityType_EntityId); ids, actors and actions are
        // prefixes (LIKE 'x%'), which the indexes on EntityId, Actor and Action answer by seeking. The database collation makes all of them case-insensitive.
        if (!string.IsNullOrWhiteSpace(q.EntityType)) { var names = AuditEntities.Matching(q.EntityType); query.Where(a => names.Contains(a.EntityType)); }
        if (!string.IsNullOrWhiteSpace(q.EntityId)) { var id = q.EntityId.Trim(); query.Where(a => a.EntityId.StartsWith(id)); }
        if (!string.IsNullOrWhiteSpace(q.Actor)) { var actor = q.Actor.Trim(); query.Where(a => a.Actor.StartsWith(actor)); }
        if (!string.IsNullOrWhiteSpace(q.Action)) { var action = q.Action.Trim(); query.Where(a => a.Action.StartsWith(action)); }
        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var text = q.Search.Trim();
            var names = AuditEntities.Matching(text);
            query.Where(a => names.Contains(a.EntityType) || a.EntityId.StartsWith(text));
        }
        if (q.From is { } f) { var from = Paging.StartOfDayUtc(f); query.Where(a => a.At >= from); }
        if (q.To is { } t) { var to = Paging.EndOfDayUtc(t); query.Where(a => a.At < to); }
    }
}
