using Ims.Application.Features.Modules;
using Ims.Domain;
namespace Ims.Application.Features.Sales;

public sealed class SalesDeliveryRules(IReadRepository<SalesOrder> orders, IDeliveredQuantityReader delivered, IModuleSwitches modules, ICallerPermissions caller) : ISalesDeliveryRules, IAppService
{
    public async Task<SalesOrder> OrderForSaveAsync(StockDocument draft, Partner? customer, IssuePurpose? purpose, CancellationToken ct)
    {
        draft.EnsureEditable();
        // Who may change a linked draft, and while the module is on, is one rule with its allowedActions (LinkedReceiptPolicy): the order is not looked at before it passes.
        await SalesDeliveryGuard.EnsureAsync(DocumentActions.Edit, caller, modules, ct);
        var order = await orders.FirstOrDefaultAsync(new SalesOrderForDeliverySpec(draft.SalesOrderId!.Value), ct) ?? throw new NotFoundException(Messages.SalesOrderNotFound);
        if (!order.CanBeDelivered) throw new ConflictException(Messages.SalesOrderNotDeliverable(order.No, order.Status));
        if (customer?.Id != order.CustomerId)
            throw new ValidationException(Messages.DeliveryCustomerLocked, new Dictionary<string, string[]> { ["partnerId"] = [Messages.DeliveryCustomerLocked] });
        if (purpose != IssuePurpose.Sale)
            throw new ValidationException(Messages.DeliveryHeaderLocked, new Dictionary<string, string[]> { ["purpose"] = [Messages.DeliveryHeaderLocked] });
        return order;
    }

    public async Task EnsureCanSubmitAsync(StockDocument draft, CancellationToken ct)
    {
        if (draft.SalesOrderId is not { } key) return;
        await SalesDeliveryGuard.EnsureAsync(DocumentActions.Submit, caller, modules, ct);
        var order = await orders.FirstOrDefaultAsync(new SalesOrderForDeliverySpec(key), ct) ?? throw new NotFoundException(Messages.SalesOrderNotFound);
        // The order's number, status and figures are shown to readers of sales orders only: anyone else is refused in general words.
        var mayRead = caller.Has(Permissions.SoRead);
        if (!order.CanBeDelivered) throw new ConflictException(mayRead ? Messages.SalesOrderNotDeliverable(order.No, order.Status) : Messages.DeliveryOrderNotOpen);
        var lines = draft.Lines.ToList();
        var over = await OverAsync(order, lines, ct);
        if (over.Count == 0) return;
        if (!mayRead) throw new ConflictException(Messages.DeliveryOverRemaining);
        throw OverDeliveriesError(order, lines, over);
    }

    public async Task EnsureWithinRemainingAsync(SalesOrder order, IReadOnlyList<StockDocumentLine> lines, CancellationToken ct)
    {
        var over = await OverAsync(order, lines, ct);
        if (over.Count > 0) throw OverDeliveriesError(order, lines, over);
    }

    private async Task<IReadOnlyList<OverDelivery>> OverAsync(SalesOrder order, IReadOnlyList<StockDocumentLine> lines, CancellationToken ct) =>
        SalesOrderQuantities.OverDeliveries(order.Lines, await delivered.PostedByLineAsync(order.Id, ct), lines.Where(l => l.SalesOrderLineNo is not null).Select(l => new DeliveryQuantity(l.SalesOrderLineNo!.Value, l.Qty, l.BaseQty)));

    private static ValidationException OverDeliveriesError(SalesOrder order, IReadOnlyList<StockDocumentLine> lines, IReadOnlyList<OverDelivery> over)
    {
        var errors = new Dictionary<string, string[]>();
        foreach (var o in over)
        {
            var orderLine = order.Lines.First(l => l.LineNo == o.LineNo);
            var firstIndex = lines.ToList().FindIndex(l => l.SalesOrderLineNo == o.LineNo);
            errors[ErrorKeys.Normalize($"Data.Lines[{firstIndex}].Qty")] = [Messages.DeliveryLineOverDelivery(o, orderLine.Product.Sku, orderLine.Uom.Code)];
        }
        return new ValidationException(errors.First().Value[0], errors);
    }
}
