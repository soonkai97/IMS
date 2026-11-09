using Ardalis.Specification;
using Ims.Domain;
namespace Ims.Application.Features.Returns;

/// <summary>A document to return, tracked, with its warehouse, partner and lines (each with its product and units): what <see cref="StockDocument.CreateReturn"/> and the line builder read.</summary>
public sealed class DocumentForReturnSpec : Specification<StockDocument>
{
    public DocumentForReturnSpec(int key) =>
        Query.Where(d => d.Id == key).Include(d => d.Warehouse).Include(d => d.Partner).Include(d => d.Lines).ThenInclude(l => l.Product).ThenInclude(p => p.Conversions)
            .Include(d => d.Lines).ThenInclude(l => l.Uom);
}
