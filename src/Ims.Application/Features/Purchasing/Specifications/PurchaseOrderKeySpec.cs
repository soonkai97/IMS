using Ardalis.Specification;
using Ims.Domain;
namespace Ims.Application.Features.Purchasing;

/// <summary>The integer key of an order from its public id: asked first by the writes that must take the order's lock before they read it.</summary>
public sealed class PurchaseOrderKeySpec : Specification<PurchaseOrder, int?>
{
    public PurchaseOrderKeySpec(Guid publicId) => Query.AsNoTracking().Where(o => o.PublicId == publicId).Select(o => (int?)o.Id);
}
