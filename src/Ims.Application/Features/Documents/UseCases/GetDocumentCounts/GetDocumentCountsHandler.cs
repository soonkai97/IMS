using Ims.Domain;
namespace Ims.Application.Features.Documents;

/// <summary>
/// The Home tiles and menu badges in one call. A fixed number of COUNT queries (at most four), each answered by an index on the
/// status or the posting time, so the cost does not depend on how many documents exist.
/// </summary>
public class GetDocumentCountsHandler(IReadRepository<StockDocument> documents, IDocumentAccess access, ICurrentUser current,
    ISystemSettings settings, IClock clock) : IQueryHandler<GetDocumentCountsQuery, DocumentCounts>
{
    public async Task<DocumentCounts> HandleAsync(GetDocumentCountsQuery q, CancellationToken ct)
    {
        var scope = access.WarehouseScope();
        var returned = await documents.CountAsync(new DraftsOfUserCountSpec(current.UserId, scope, returnedOnly: true), ct);
        var drafts = await documents.CountAsync(new DraftsOfUserCountSpec(current.UserId, scope, returnedOnly: false), ct);

        // "Today" is the Malaysia-local day (BR-10), expressed as a UTC range.
        var today = DateOnly.FromDateTime(clock.UtcNow.AddHours(Limits.MalaysiaUtcOffsetHours));
        var posted = await documents.CountAsync(new PostedBetweenCountSpec(Paging.StartOfDayUtc(today), Paging.EndOfDayUtc(today), scope), ct);

        var waiting = Permissions.Has(current.Role, Permissions.DocumentApprove)
            ? await documents.CountAsync(new ApprovalsWaitingCountSpec(current.Role, current.UserId, (await settings.GetAsync(ct)).AllowSelfApproval, scope), ct)
            : 0;
        return new DocumentCounts(returned, drafts, posted, waiting);
    }
}
