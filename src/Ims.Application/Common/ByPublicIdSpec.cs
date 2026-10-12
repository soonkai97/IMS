using Ardalis.Specification;
using Ims.Domain;
namespace Ims.Application;

/// <summary>Look-up by the public id (the GUID a client sends), shared by every <see cref="PublicEntity"/>. Soft-deleted rows are hidden, as for any query.</summary>
public sealed class ByPublicIdSpec<T> : Specification<T> where T : PublicEntity
{
    public ByPublicIdSpec(Guid publicId, bool tracked = true)
    {
        Query.Where(e => e.PublicId == publicId);
        if (!tracked) Query.AsNoTracking();
    }
}
