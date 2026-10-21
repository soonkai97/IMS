using Ims.Application.Features.Modules;
using Ims.Domain;
namespace Ims.Application.Features.Documents;

/// <summary>Loads the products a document refers to; the lines themselves (unit conversion, costing, product checks) are built by the domain.</summary>
public class DocumentLineBuilder(IReadRepository<Product> products, IIdMap<Uom> uomIds, ICallerPermissions caller, IModuleSwitches modules) : IDocumentLineBuilder, IAppService
{
    public async Task<List<StockDocumentLine>> BuildAsync(DocumentType type, IReadOnlyList<DocumentLineInput> inputs, CancellationToken ct,
        IEnumerable<StockDocumentLine>? existing = null, PurchaseOrder? order = null)
    {
        if (order is null) await EnsureNoOrderLinesAsync(inputs, ct);

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

        var lines = new List<StockDocumentLine>(inputs.Count);
        var orderErrors = new Dictionary<string, string[]>();
        for (var index = 0; index < inputs.Count; index++)
        {
            var i = inputs[index];
            if (!byId.TryGetValue(i.ProductId, out var product)) throw new ValidationException(Messages.ProductMissing(i.ProductId));
            int? uomId = i.UomId is { } unit ? units.GetValueOrDefault(unit, IdMapExtensions.NoRow) : null;

            if (order is not null)
            {
                // A receipt against an order: the line names an order line, and is priced at the order's price (a caller who may enter cost can send another). It is never at the average.
                var (orderLine, problem) = OrderLineProblem(order, i.PurchaseOrderLineNo, product, uomId ?? product.BaseUomId);
                if (problem is not null) orderErrors[ErrorKeys.Normalize($"Data.Lines[{index}].PurchaseOrderLineNo")] = [problem];
                else
                {
                    var price = costIsUsed && i.UnitCost is { } typed ? typed : orderLine!.UnitPrice;
                    lines.Add(StockDocumentLine.Create(product, type, i.Qty, uomId, price, i.Remark, costFromAverage: false, purchaseOrderLineNo: orderLine!.LineNo));
                }
                continue;
            }

            var key = (product.Id, uomId ?? product.BaseUomId);
            var keptAtAverage = costIsUsed && i.UnitCost is null && atAverage.GetValueOrDefault(key) > 0;
            if (keptAtAverage) atAverage[key]--;
            lines.Add(StockDocumentLine.Create(product, type, i.Qty, uomId, costIsUsed ? i.UnitCost : null, i.Remark, costFromAverage: !costIsUsed || keptAtAverage));
        }
        if (orderErrors.Count > 0) throw new ValidationException(orderErrors.First().Value[0], orderErrors);
        return lines;
    }

    /// <summary>The order line a receipt line names, or the sentence that says why it cannot: no number, no such line, another product, another unit (the price is per ordered unit).</summary>
    private static (PurchaseOrderLine? Line, string? Problem) OrderLineProblem(PurchaseOrder order, int? lineNo, Product product, int uomId)
    {
        if (lineNo is null) return (null, Messages.ReceiptLineNeedsOrderLine);
        var line = order.Lines.FirstOrDefault(l => l.LineNo == lineNo);
        if (line is null) return (null, Messages.ReceiptLineUnknownOrderLine(lineNo.Value, order.No));
        if (line.ProductId != product.Id) return (line, Messages.ReceiptLineWrongProduct(line.LineNo, line.Product.Sku));
        if (line.UomId != uomId) return (line, Messages.ReceiptLineWrongUnit(line.LineNo, line.Uom.Code));
        return (line, null);
    }

    /// <summary>
    /// A document that is not linked to an order takes no order line numbers: a 400 "not available" while the Purchasing module is off, "not linked" while it is on. Asked only when a line carries
    /// the field, so every ordinary document costs nothing extra.
    /// </summary>
    private async Task EnsureNoOrderLinesAsync(IReadOnlyList<DocumentLineInput> inputs, CancellationToken ct)
    {
        var carried = inputs.Select((l, n) => (Field: ErrorKeys.Normalize($"Data.Lines[{n}].PurchaseOrderLineNo"), Value: l.PurchaseOrderLineNo?.ToString())).Where(c => c.Value is not null).ToList();
        if (carried.Count == 0) return;
        await modules.AcceptAsync(ModuleCodes.Purchasing, carried, ct); // throws while the module is off
        throw new ValidationException(Messages.ReceiptNotLinked, carried.ToDictionary(c => c.Field, _ => new[] { Messages.ReceiptNotLinked }));
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
