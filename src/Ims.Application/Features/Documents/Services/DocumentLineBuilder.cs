using Ims.Domain;
namespace Ims.Application.Features.Documents;

/// <summary>Loads the products a document refers to; the lines themselves (unit conversion, costing, product checks) are built by the domain.</summary>
public class DocumentLineBuilder(IReadRepository<Product> products, ICallerPermissions caller) : IDocumentLineBuilder, IAppService
{
    public async Task<List<StockDocumentLine>> BuildAsync(DocumentType type, IReadOnlyList<DocumentLineInput> inputs, CancellationToken ct)
    {
        // The cost of a line is used only on a receipt saved by someone who may enter cost. An issue takes the average and a Warehouse staff receipt takes the
        // last known cost, whatever the request carried, so a cost that is not used is not judged (and not stored).
        var costIsUsed = type == DocumentType.Receipt && caller.Has(Permissions.CostView);
        if (costIsUsed) EnsureCostsAreMoney(inputs);

        var ids = inputs.Select(i => i.ProductId).Distinct().ToList();
        var byId = (await products.ListAsync(new ProductsForLinesSpec(ids), ct)).ToDictionary(p => p.Id);

        return inputs.Select(i =>
        {
            if (!byId.TryGetValue(i.ProductId, out var product)) throw new ValidationException(Messages.ProductMissing(i.ProductId));
            return StockDocumentLine.Create(product, type, i.Qty, i.UomId, costIsUsed ? i.UnitCost : null, i.Remark);
        }).ToList();
    }

    /// <summary>Money has two decimal places: a unit cost with a fraction of a cent is a 400 with a field error on that line (all of them, not the first).</summary>
    private static void EnsureCostsAreMoney(IReadOnlyList<DocumentLineInput> inputs)
    {
        var errors = new Dictionary<string, string[]>();
        for (var i = 0; i < inputs.Count; i++)
            if (inputs[i].UnitCost is { } cost && !Money.IsExact(cost)) errors[$"Data.Lines[{i}].UnitCost"] = [Messages.CostTooPrecise];
        if (errors.Count > 0) throw new ValidationException(Messages.CostTooPrecise, errors);
    }
}
