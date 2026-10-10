using Ims.Domain;
namespace Ims.Application.Features.Audit;

/// <param name="AfterId">Seek paging: return entries older than this id.</param>
/// <param name="IncludeTotal">False skips the row count.</param>
public record AuditQueryFilter(string? EntityType, string? Actor, DateOnly? From, DateOnly? To, int Page = 1,
    int PageSize = Limits.DefaultListPageSize, long? AfterId = null, bool IncludeTotal = true);
