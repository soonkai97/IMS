using Ardalis.Specification;
using Ims.Domain;
namespace Ims.Application;

public sealed class ByIdsSpec<T> : Specification<T> where T : Entity
{
    public ByIdsSpec(IReadOnlyCollection<int> ids, bool tracked = true)
    {
        Query.Where(e => ids.Contains(e.Id));
        if (!tracked) Query.AsNoTracking();
    }
}
