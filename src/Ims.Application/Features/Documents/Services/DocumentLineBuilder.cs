using Ims.Domain;
namespace Ims.Application.Features.Documents;

/// <summary>Loads the products a document refers to; the lines themselves (unit conversion, costing, product checks) are built by the domain.</summary>
public class DocumentLineBuilder(IReadRepository<Product> products, IIdMap<Uom> uomIds, ICallerPermissions caller) : IDocumentLineBuilder, IAppService
{
    public async Task<List<StockDocumentLine>> BuildAsync(DocumentType type, IReadOnlyList<DocumentLineInput> inputs, CancellationToken ct,
        IEnumerable<StockDocumentLine>? existing = null)
    {
        // The cost of a line is used only on a receipt saved by someone who may enter cost. An issue takes the average and a Warehouse staff receipt takes the
        // last known cost, whatever the request carried, so a cost that is not used is not judged (and not stored).
        var costIsUsed = type == DocumentType.Receipt && caller.Has(Permissions.CostView);
        if (costIsUsed) EnsureCostsAreMoney(inputs);

        // A draft being saved again: a line that was at the average (nobody typed its cost) and comes back with no cost stays at the average. The lines have no
        // identity in the request, so they are matched by product and unit; a line with no cost that matches none is a new line without a cost (zero).
        var atAverage = costIsUsed
            ? (existing ?? []).Where(l => l.CostFromAverage).GroupBy(l => (l.ProductId, l.UomId)).ToDictionary(g => g.Key, g => g.Count())
            : new Dictionary<(int, int), int>();

        var ids = inputs.Select(i => i.ProductId).Distinct().ToList();
        var byId = (await products.ListAsync(new ProductsForLinesSpec(ids), ct)).ToDictionary(p => p.PublicId);

        // The units a client names are public ids; a unit that does not exist becomes the id of no row, which the domain refuses like any unit the product cannot convert.
        var units = await uomIds.ToInternalAsync(inputs.Where(i => i.UomId is not null).Select(i => i.UomId!.Value), ct);

        return inputs.Select(i =>
        {
            if (!byId.TryGetValue(i.ProductId, out var product)) throw new ValidationException(Messages.ProductMissing(i.ProductId));
            int? uomId = i.UomId is { } unit ? units.GetValueOrDefault(unit, IdMapExtensions.NoRow) : null;
            var key = (product.Id, uomId ?? product.BaseUomId);
            var keptAtAverage = costIsUsed && i.UnitCost is null && atAverage.GetValueOrDefault(key) > 0;
            if (keptAtAverage) atAverage[key]--;
            return StockDocumentLine.Create(product, type, i.Qty, uomId, costIsUsed ? i.UnitCost : null, i.Remark, costFromAverage: !costIsUsed || keptAtAverage);
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
