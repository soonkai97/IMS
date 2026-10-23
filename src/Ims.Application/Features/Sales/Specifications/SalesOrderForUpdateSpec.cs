using Ardalis.Specification;
using Ims.Domain;
namespace Ims.Application.Features.Sales;

/// <summary>Tracked sales order with its lines, for any command that changes it.</summary>
public sealed class SalesOrderForUpdateSpec : Specification<SalesOrder>
{
    public SalesOrderForUpdateSpec(Guid publicId) => Query.Where(o => o.PublicId == publicId).Include(o => o.Lines);
}
