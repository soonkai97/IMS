using Ims.Domain;
namespace Ims.Application.Features.Sales;

public class SalesOrderPriceAudit(IPriceLookup lookup, IAuditTrail audit) : ISalesOrderPriceAudit, IAppService
{
    // The Source key is not money (it says which list answered, Customer or Default), so unlike the two price keys it carries no AuditFields tag: any reader of the audit log may see it.
    public static List<SavedLine> Snapshot(SalesOrder order) => order.Lines.Select(l => new SavedLine(l.ProductId, l.UomId, l.SellingPrice)).ToList();

    public async Task RecordAsync(SalesOrder order, IReadOnlyCollection<SavedLine> before, int? beforeCustomerId, CancellationToken ct)
    {
        var listed = await lookup.FindAsync(order.CustomerId, order.Lines.Select(l => (l.ProductId, l.UomId)).Distinct().ToList(), ct);
        var already = beforeCustomerId == order.CustomerId ? before.ToList() : []; // a line is "the same" once per earlier line
        foreach (var line in order.Lines)
        {
            if (line.SellingPrice <= 0) continue;
            var index = already.IndexOf(new SavedLine(line.ProductId, line.UomId, line.SellingPrice));
            if (index >= 0)
            {
                already.RemoveAt(index);
                continue;
            }
            if (!listed.TryGetValue((line.ProductId, line.UomId), out var price) || price.Price == line.SellingPrice) continue;

            audit.Record(AuditActions.SalesOrderPriceChanged, AuditEntities.SalesOrder, order.No, after: new Dictionary<string, object?>
            {
                ["Line"] = line.LineNo, [nameof(line.ProductId)] = line.ProductId, [nameof(line.UomId)] = line.UomId, ["Source"] = price.Source,
                [AuditFields.ListedPrice] = price.Price, [AuditFields.EnteredPrice] = line.SellingPrice
            });
        }
    }
}
