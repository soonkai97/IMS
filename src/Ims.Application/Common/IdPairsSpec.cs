using Ardalis.Specification;
using Ims.Domain;
namespace Ims.Application;

/// <summary>
/// Only the two ids of the rows asked for, no entity loaded. <see cref="ForPublicIds"/> is for ids that come from a client, so a soft-deleted row is hidden like in
/// any query (it counts as not found). <see cref="ForIds"/> is for ids that come from a row already read (a foreign key): the row it names may be soft-deleted,
/// and the response must still name it, so the filter is switched off.
/// </summary>
public sealed class IdPairsSpec<T> : Specification<T, IdPair> where T : PublicEntity
{
    private IdPairsSpec() => Query.AsNoTracking();

    public static IdPairsSpec<T> ForPublicIds(IReadOnlyCollection<Guid> publicIds)
    {
        var spec = new IdPairsSpec<T>();
        spec.Query.Where(e => publicIds.Contains(e.PublicId));
        spec.Query.Select(e => new IdPair(e.Id, e.PublicId));
        return spec;
    }

    public static IdPairsSpec<T> ForIds(IReadOnlyCollection<int> ids)
    {
        var spec = new IdPairsSpec<T>();
        spec.Query.IgnoreQueryFilters().Where(e => ids.Contains(e.Id));
        spec.Query.Select(e => new IdPair(e.Id, e.PublicId));
        return spec;
    }
}
