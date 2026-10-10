using Ardalis.Specification;
using Ims.Domain;
namespace Ims.Application;

/// <summary>Look-up by integer key, shared by every entity with an <see cref="Entity.Id"/>.</summary>
public sealed class ByIdSpec<T> : Specification<T> where T : Entity
{
    public ByIdSpec(int id, bool tracked = true)
    {
        Query.Where(e => e.Id == id);
        if (!tracked) Query.AsNoTracking();
    }
}
