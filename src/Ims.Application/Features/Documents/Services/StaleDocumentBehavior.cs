using Ims.Domain;
namespace Ims.Application.Features.Documents;

/// <summary>
/// Puts a name to a stale-document conflict: the thrown exception knows the id of the user who last changed the document; this step looks up their
/// display name after the failed request has finished, so the client can say "{name} changed this document at {time}" (UX E4).
/// </summary>
public sealed class StaleDocumentBehavior(IReadRepository<User> users) : IPipelineBehavior
{
    public async Task<TResult> HandleAsync<TResult>(IRequest<TResult> request, Func<Task<TResult>> next, CancellationToken ct)
    {
        try
        {
            return await next();
        }
        catch (StaleDocumentException stale) when (stale is { ChangedByName: null, ChangedById: { } id })
        {
            var name = await users.FirstOrDefaultAsync(new UserDisplayNameSpec(id), ct);
            throw new StaleDocumentException(stale.DocumentNo, stale.ChangedById, stale.ChangedAt, name);
        }
    }
}
