using Ardalis.Specification;
using Ims.Domain;
namespace Ims.Application.Features.Audit;

/// <summary>Filter only: used for the total count.</summary>
public sealed class AuditLogsSpec : Specification<AuditLog>
{
    public AuditLogsSpec(AuditQueryFilter q) => AuditLogFilters.Apply(Query, q);
}
