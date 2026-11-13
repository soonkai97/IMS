using Ims.Application.Features.Modules;
using Ims.Domain;
namespace Ims.Application.Features.Documents;

/// <summary>Loads the products a document refers to; the lines themselves (unit conversion, costing, product checks) are built by the domain.</summary>
public sealed class DocumentLineBuilder(IReadRepository<Product> products, IIdMap<Uom> uomIds, ICallerPermissions caller, IModuleSwitches modules) : IDocumentLineBuilder, IAppService
{
    public async Task<List<StockDocumentLine>> BuildAsync(DocumentType type, IReadOnlyList<DocumentLineInput> inputs, CancellationToken ct,
        IEnumerable<StockDocumentLine>? existing = null, LinkedOrder? link = null)
    {
        var order = (link as PurchaseLinkedOrder)?.Order;
        var salesOrder = (link as SalesLinkedOrder)?.Order;
        var original = (link as ReturnLinkedOriginal)?.Original;
        var originalLines = original is null ? null : DocumentLinePositions.Of(original.Lines).ToDictionary(p => p.LineNo, p => p.Line); // one ordering helper: the position is the key order
        if (link is null) await EnsureNoOrderLinesAsync(inputs, ct);

        // The cost of a line is used only on a receipt saved by someone who may enter cost. An issue takes the average and a Warehouse staff receipt takes the
        // last known cost, whatever the request carried, so a cost that is not used is not judged (and not stored).
        var costIsUsed = original is null && type == DocumentType.Receipt && caller.Has(Permissions.CostView);
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

            if (original is not null)
            {
                // A return: the line names a line of the document it returns, with that line's product and unit (so the quantity is exact). A customer return takes the cost the goods left at (the original
                // line's cost per base unit), a supplier return the average: a typed cost is discarded, whoever saves.
                var (returnedLine, returnProblem) = ReturnLineProblem(original, originalLines!, i.ReturnOfLineNo, product, uomId ?? product.BaseUomId);
                if (i.PurchaseOrderLineNo is not null) orderErrors[ErrorKeys.Normalize($"Data.Lines[{index}].PurchaseOrderLineNo")] = [Messages.ReceiptNotLinked];
                else if (i.SalesOrderLineNo is not null) orderErrors[ErrorKeys.Normalize($"Data.Lines[{index}].SalesOrderLineNo")] = [Messages.DeliveryNotLinked];
                else if (returnProblem is not null) orderErrors[ErrorKeys.Normalize($"Data.Lines[{index}].ReturnOfLineNo")] = [returnProblem];
                else lines.Add(StockDocumentLine.CreateReturn(product, type, i.Qty, uomId, i.Remark, i.ReturnOfLineNo!.Value, returnedLine!.UnitCost));
                continue;
            }

            if (salesOrder is not null)
            {
                // A delivery against a sales order: the line names an order line with its product and unit. It carries no price: an issue is valued at the average whatever the order says.
                var (soLine, soProblem) = SalesOrderLineProblem(salesOrder, i.SalesOrderLineNo, product, uomId ?? product.BaseUomId);
                if (i.ReturnOfLineNo is not null) orderErrors[ErrorKeys.Normalize($"Data.Lines[{index}].ReturnOfLineNo")] = [Messages.ReturnNotLinked];
                else if (i.PurchaseOrderLineNo is not null) orderErrors[ErrorKeys.Normalize($"Data.Lines[{index}].PurchaseOrderLineNo")] = [Messages.DeliveryNotLinked];
                else if (soProblem is not null) orderErrors[ErrorKeys.Normalize($"Data.Lines[{index}].SalesOrderLineNo")] = [soProblem];
                else lines.Add(StockDocumentLine.Create(product, type, i.Qty, uomId, null, i.Remark, costFromAverage: true, salesOrderLineNo: soLine!.LineNo));
                continue;
            }

            if (order is not null)
            {
                // A receipt against an order: the line names an order line, and is priced at the order's price (a caller who may enter cost can send another). It is never at the average.
                var (orderLine, problem) = OrderLineProblem(order, i.PurchaseOrderLineNo, product, uomId ?? product.BaseUomId);
                if (i.ReturnOfLineNo is not null) orderErrors[ErrorKeys.Normalize($"Data.Lines[{index}].ReturnOfLineNo")] = [Messages.ReturnNotLinked];
                else if (i.SalesOrderLineNo is not null) orderErrors[ErrorKeys.Normalize($"Data.Lines[{index}].SalesOrderLineNo")] = [Messages.DeliveryNotLinked];
                else if (problem is not null) orderErrors[ErrorKeys.Normalize($"Data.Lines[{index}].PurchaseOrderLineNo")] = [problem];
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

    /// <summary>The sales order line a delivery line names, or the sentence that says why it cannot: no number, no such line, another product, another unit (the price is per ordered unit).</summary>
    private static (SalesOrderLine? Line, string? Problem) SalesOrderLineProblem(SalesOrder order, int? lineNo, Product product, int uomId)
    {
        if (lineNo is null) return (null, Messages.DeliveryLineNeedsOrderLine);
        var line = order.Lines.FirstOrDefault(l => l.LineNo == lineNo);
        if (line is null) return (null, Messages.DeliveryLineUnknownOrderLine(lineNo.Value, order.No));
        if (line.ProductId != product.Id) return (line, Messages.DeliveryLineWrongProduct(line.LineNo, line.Product.Sku));
        if (line.UomId != uomId) return (line, Messages.DeliveryLineWrongUnit(line.LineNo, line.Uom.Code));
        return (line, null);
    }

    /// <summary>The line of the original a return line names (by position), or the sentence that says why it cannot: no number, no such line, another product, another unit (the quantity is exact in the original unit).</summary>
    private static (StockDocumentLine? Line, string? Problem) ReturnLineProblem(StockDocument original, IReadOnlyDictionary<int, StockDocumentLine> positions, int? lineNo, Product product, int uomId)
    {
        if (lineNo is null) return (null, Messages.ReturnLineNeedsOriginalLine);
        if (!positions.TryGetValue(lineNo.Value, out var line)) return (null, Messages.ReturnLineUnknownOriginalLine(lineNo.Value, original.No));
        if (line.ProductId != product.Id) return (line, Messages.ReturnLineWrongProduct(lineNo.Value, line.Product.Sku));
        if (line.UomId != uomId) return (line, Messages.ReturnLineWrongUnit(lineNo.Value, line.Uom.Code));
        return (line, null);
    }

    /// <summary>
    /// A document that is not linked to an order takes no order line numbers: a 400 "not available" while the owning module (Purchasing or Sales) is off, "not linked" while it is on. Asked only when a line
    /// carries the field, so every ordinary document costs nothing extra.
    /// </summary>
    private async Task EnsureNoOrderLinesAsync(IReadOnlyList<DocumentLineInput> inputs, CancellationToken ct)
    {
        var purchase = inputs.Select((l, n) => (Field: ErrorKeys.Normalize($"Data.Lines[{n}].PurchaseOrderLineNo"), Value: l.PurchaseOrderLineNo?.ToString())).Where(c => c.Value is not null).ToList();
        var sales = inputs.Select((l, n) => (Field: ErrorKeys.Normalize($"Data.Lines[{n}].SalesOrderLineNo"), Value: l.SalesOrderLineNo?.ToString())).Where(c => c.Value is not null).ToList();
        var returns = inputs.Select((l, n) => (Field: ErrorKeys.Normalize($"Data.Lines[{n}].ReturnOfLineNo"), Value: l.ReturnOfLineNo?.ToString())).Where(c => c.Value is not null).ToList();
        if (purchase.Count == 0 && sales.Count == 0 && returns.Count == 0) return;
        if (purchase.Count > 0) await modules.AcceptAsync(ModuleCodes.Purchasing, purchase, ct); // throws while the module is off
        if (sales.Count > 0) await modules.AcceptAsync(ModuleCodes.Sales, sales, ct);
        if (returns.Count > 0) await modules.AcceptAsync(ModuleCodes.Returns, returns, ct);
        var errors = new Dictionary<string, string[]>();
        foreach (var c in purchase) errors[c.Field] = [Messages.ReceiptNotLinked];
        foreach (var c in sales) errors[c.Field] = [Messages.DeliveryNotLinked];
        foreach (var c in returns) errors[c.Field] = [Messages.ReturnNotLinked];
        throw new ValidationException(errors.First().Value[0], errors);
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
