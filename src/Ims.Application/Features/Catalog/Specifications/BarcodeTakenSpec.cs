using Ardalis.Specification;
using FluentValidation;
using Ims.Domain;
namespace Ims.Application.Features.Catalog;

public sealed class BarcodeTakenSpec : Specification<ProductBarcode>
{
    public BarcodeTakenSpec(IReadOnlyCollection<string> codes, int exceptProductId) =>
        Query.Where(b => codes.Contains(b.Barcode) && b.ProductId != exceptProductId).AsNoTracking();
}
