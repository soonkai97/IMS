using Ardalis.Specification;
using Ims.Domain;
namespace Ims.Application.Features.Purchasing;

/// <summary>Tracked purchase order with its lines, for any command that changes it.</summary>
public sealed class PurchaseOrderForUpdateSpec : Specification<PurchaseOrder>
{
    public PurchaseOrderForUpdateSpec(Guid publicId) => Query.Where(o => o.PublicId == publicId).Include(o => o.Lines);
}
