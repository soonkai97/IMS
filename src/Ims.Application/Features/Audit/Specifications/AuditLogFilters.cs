using Ardalis.Specification;
using Ims.Domain;
namespace Ims.Application.Features.Audit;

internal static class AuditLogFilters
{
    public static void Apply(ISpecificationBuilder<AuditLog> query, AuditQueryFilter q)
    {
        query.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(q.EntityType)) query.Where(a => a.EntityType == q.EntityType);
        if (!string.IsNullOrWhiteSpace(q.Actor)) query.Where(a => a.Actor == q.Actor);
        if (q.From is { } f) { var from = Paging.StartOfDayUtc(f); query.Where(a => a.At >= from); }
        if (q.To is { } t) { var to = Paging.EndOfDayUtc(t); query.Where(a => a.At < to); }
    }
}
