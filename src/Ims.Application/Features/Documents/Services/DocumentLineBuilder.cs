using Ims.Domain;
namespace Ims.Application.Features.Documents;

/// <summary>Loads the products a document refers to; the lines themselves (unit conversion, costing, product checks) are built by the domain.</summary>
public class DocumentLineBuilder(IReadRepository<Product> products) : IDocumentLineBuilder, IAppService
{
    public async Task<List<StockDocumentLine>> BuildAsync(DocumentType type, IReadOnlyList<DocumentLineInput> inputs, CancellationToken ct)
    {
        var ids = inputs.Select(i => i.ProductId).Distinct().ToList();
        var byId = (await products.ListAsync(new ProductsForLinesSpec(ids), ct)).ToDictionary(p => p.Id);

        return inputs.Select(i =>
        {
            if (!byId.TryGetValue(i.ProductId, out var product)) throw new ValidationException(Messages.ProductMissing(i.ProductId));
            return StockDocumentLine.Create(product, type, i.Qty, i.UomId, i.UnitCost, i.Remark);
        }).ToList();
    }
}
