using Ims.Application.Features.Modules;
using Ims.Domain;
namespace Ims.Application.Features.Purchasing;

public class ReceiptLinkRules(IReadRepository<PurchaseOrder> orders, IPurchaseOrderReceiving receiving, IModuleSwitches modules, ICallerPermissions caller) : IReceiptLinkRules, IAppService
{
    public async Task<PurchaseOrder> OrderForSaveAsync(StockDocument draft, Partner? supplier, CancellationToken ct)
    {
        draft.EnsureEditable();
        // Who may change a linked draft, and while the module is on, is one rule with its allowedActions (LinkedReceiptPolicy): the order is not looked at before it passes.
        await LinkedReceiptGuard.EnsureAsync(DocumentActions.Edit, caller, modules, ct);
        var order = await orders.FirstOrDefaultAsync(new PurchaseOrderForReceiptSpec(draft.PurchaseOrderId!.Value), ct) ?? throw new NotFoundException(Messages.PurchaseOrderNotFound);
        if (!order.CanBeReceived) throw new ConflictException(Messages.PurchaseOrderNotReceivable(order.No, order.Status));
        if (supplier?.Id != order.SupplierId)
            throw new ValidationException(Messages.ReceiptSupplierLocked, new Dictionary<string, string[]> { ["partnerId"] = [Messages.ReceiptSupplierLocked] });
        return order;
    }

    public async Task EnsureCanSubmitAsync(StockDocument draft, CancellationToken ct)
    {
        if (draft.PurchaseOrderId is not { } key) return;
        await LinkedReceiptGuard.EnsureAsync(DocumentActions.Submit, caller, modules, ct);
        var order = await orders.FirstOrDefaultAsync(new PurchaseOrderForReceiptSpec(key), ct) ?? throw new NotFoundException(Messages.PurchaseOrderNotFound);
        // The order's number, status and figures are shown to readers of purchase orders only: anyone else is refused in general words (the same rule as at posting).
        var mayRead = caller.Has(Permissions.PoRead);
        if (!order.CanBeReceived) throw new ConflictException(mayRead ? Messages.PurchaseOrderNotReceivable(order.No, order.Status) : Messages.ReceiptOrderNotOpen);
        var lines = draft.Lines.ToList();
        var over = await OverAsync(order, lines, ct);
        if (over.Count == 0) return;
        if (!mayRead) throw new ConflictException(Messages.ReceiptOverRemaining);
        throw OverReceiptsError(order, lines, over);
    }

    public async Task EnsureWithinRemainingAsync(PurchaseOrder order, IReadOnlyList<StockDocumentLine> lines, CancellationToken ct)
    {
        var over = await OverAsync(order, lines, ct);
        if (over.Count > 0) throw OverReceiptsError(order, lines, over);
    }

    private async Task<IReadOnlyList<OverReceipt>> OverAsync(PurchaseOrder order, IReadOnlyList<StockDocumentLine> lines, CancellationToken ct) =>
        PurchaseOrderQuantities.OverReceipts(order.Lines, await receiving.ReceivedAsync(order.Id, ct), lines.Select(l => new ReceiptQuantity(l.PurchaseOrderLineNo!.Value, l.Qty, l.BaseQty)));

    private static ValidationException OverReceiptsError(PurchaseOrder order, IReadOnlyList<StockDocumentLine> lines, IReadOnlyList<OverReceipt> over)
    {
        var errors = new Dictionary<string, string[]>();
        foreach (var o in over)
        {
            var orderLine = order.Lines.First(l => l.LineNo == o.LineNo);
            var firstIndex = lines.ToList().FindIndex(l => l.PurchaseOrderLineNo == o.LineNo);
            errors[ErrorKeys.Normalize($"Data.Lines[{firstIndex}].Qty")] = [Messages.ReceiptLineOverReceipt(o, orderLine.Product.Sku, orderLine.Uom.Code)];
        }
        return new ValidationException(errors.First().Value[0], errors);
    }
}
