using Ardalis.Specification;
using Ims.Domain;
namespace Ims.Application;

/// <summary>Look-up of several rows by public id in one query. Soft-deleted rows are hidden, as for any query.</summary>
public sealed class PublicIdsSpec<T> : Specification<T> where T : PublicEntity
{
    public PublicIdsSpec(IReadOnlyCollection<Guid> publicIds, bool tracked = true)
    {
        Query.Where(e => publicIds.Contains(e.PublicId));
        if (!tracked) Query.AsNoTracking();
    }
}
