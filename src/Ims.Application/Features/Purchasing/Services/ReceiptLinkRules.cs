using Ims.Application.Features.Modules;
using Ims.Domain;
namespace Ims.Application.Features.Purchasing;

public class ReceiptLinkRules(IReadRepository<PurchaseOrder> orders, IPurchaseOrderReceiving receiving, IModuleSwitches modules, ICallerPermissions caller) : IReceiptLinkRules, IAppService
{
    public async Task<PurchaseOrder> OrderForSaveAsync(StockDocument draft, Partner? supplier, CancellationToken ct)
    {
        draft.EnsureEditable();
        // The link and the order's figures (ordered, received) are shown to readers of purchase orders only, so only they can change a linked draft: anyone else could not even name its order lines.
        if (!caller.Has(Permissions.PoRead)) throw new ForbiddenException(Messages.ReceiptNeedsPoRead);
        if (!await modules.IsEnabledAsync(ModuleCodes.Purchasing, ct)) throw new ConflictException(Messages.ReceiptModuleOff);
        var order = await orders.FirstOrDefaultAsync(new PurchaseOrderForReceiptSpec(draft.PurchaseOrderId!.Value), ct) ?? throw new NotFoundException(Messages.PurchaseOrderNotFound);
        if (!order.CanBeReceived) throw new ConflictException(Messages.PurchaseOrderNotReceivable(order.No, order.Status));
        if (supplier?.Id != order.SupplierId)
            throw new ValidationException(Messages.ReceiptSupplierLocked, new Dictionary<string, string[]> { ["partnerId"] = [Messages.ReceiptSupplierLocked] });
        return order;
    }

    public async Task EnsureCanSubmitAsync(StockDocument draft, CancellationToken ct)
    {
        if (draft.PurchaseOrderId is not { } key) return;
        if (!await modules.IsEnabledAsync(ModuleCodes.Purchasing, ct)) throw new ConflictException(Messages.ReceiptModuleOff);
        var order = await orders.FirstOrDefaultAsync(new PurchaseOrderForReceiptSpec(key), ct) ?? throw new NotFoundException(Messages.PurchaseOrderNotFound);
        if (!order.CanBeReceived) throw new ConflictException(Messages.PurchaseOrderNotReceivable(order.No, order.Status));
        await EnsureWithinRemainingAsync(order, draft.Lines.ToList(), ct);
    }

    public async Task EnsureWithinRemainingAsync(PurchaseOrder order, IReadOnlyList<StockDocumentLine> lines, CancellationToken ct)
    {
        var received = await receiving.ReceivedAsync(order.Id, ct);
        var over = PurchaseOrderQuantities.OverReceipts(order.Lines, received, lines.Select(l => new ReceiptQuantity(l.PurchaseOrderLineNo!.Value, l.Qty, l.BaseQty)));
        if (over.Count == 0) return;

        var errors = new Dictionary<string, string[]>();
        foreach (var o in over)
        {
            var orderLine = order.Lines.First(l => l.LineNo == o.LineNo);
            var firstIndex = lines.ToList().FindIndex(l => l.PurchaseOrderLineNo == o.LineNo);
            errors[ErrorKeys.Normalize($"Data.Lines[{firstIndex}].Qty")] = [Messages.ReceiptLineOverReceipt(o, orderLine.Product.Sku, orderLine.Uom.Code)];
        }
        throw new ValidationException(errors.First().Value[0], errors);
    }
}
