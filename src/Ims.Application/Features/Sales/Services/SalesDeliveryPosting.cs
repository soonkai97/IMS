using Ims.Application.Features.Documents;
using Ims.Application.Features.Modules;
using Ims.Domain;
namespace Ims.Application.Features.Sales;

/// <summary>
/// The sales-order part of posting a delivery that was created from an order (PL3.7), kept out of <c>PostDocumentHandler</c> so that a document that is not linked runs exactly the statements it ran before.
/// Lock order: the order's lock FIRST (nothing has locked stock yet; the balance loader does it after this returns), then the order and the posted quantities are read fresh (the lock is what makes them the
/// truth), then the checks. This is the AUTHORITATIVE over-delivery check: the ones at save and submit are advice, they hold nothing.
/// </summary>
public class SalesDeliveryPosting(IReadRepository<SalesOrder> orders, IReadRepository<StockDocument> documents, IDeliveredQuantityReader delivered, IModuleSwitches modules, ITransactionLocker locker, ICurrentUser current, IClock clock,
    IAuditTrail audit, ICallerPermissions caller) : IDocumentLinkPosting, IAppService
{
    public DocumentLinkKind Kind => DocumentLinkKind.SalesOrder;

    public async Task<LinkedPostingState?> BeforePostAsync(StockDocument document, CancellationToken ct)
    {
        if (document.SalesOrderId is not { } key) return null;

        // First lock of the transaction: nothing has locked stock yet (the balance loader does it after this returns).
        await locker.LockSalesOrderAsync(key, ct);
        // The document was read before the lock. If it changed while this posting waited (the same delivery posted, reversed or cancelled by someone else) the whole posting starts again on the new state, which
        // then answers "already posted" in its own words, instead of judging a delivery that is already counted against the order and calling it an over-delivery.
        var stamp = await documents.FirstOrDefaultAsync(new DocumentStampSpec(document.Id), ct);
        if (stamp is null || !stamp.RowVersion.AsSpan().SequenceEqual(document.RowVersion)) throw new ConcurrencyException(Messages.ConcurrencyConflict);
        await SalesDeliveryGuard.EnsureAsync(DocumentActions.Post, caller, modules, ct);
        var order = await orders.FirstOrDefaultAsync(new SalesOrderForDeliverySpec(key), ct) ?? throw new NotFoundException(Messages.SalesOrderNotFound);
        // The order's number, status and figures are shown to readers of sales orders only (as when the delivery was made), so anyone else is refused in general words.
        var mayRead = caller.Has(Permissions.SoRead);
        if (!order.CanBeDelivered) throw new ConflictException(mayRead ? Messages.SalesOrderNotDeliverable(order.No, order.Status) : Messages.DeliveryOrderNotOpen);

        var done = await delivered.PostedByLineAsync(key, ct);
        var thisDelivery = document.Lines.Where(l => l.SalesOrderLineNo is not null).Select(l => new DeliveryQuantity(l.SalesOrderLineNo!.Value, l.Qty, l.BaseQty)).ToList();
        var over = SalesOrderQuantities.OverDeliveries(order.Lines, done, thisDelivery);
        if (over.Count > 0) throw mayRead ? OverDeliveryFor(order, over) : new ConflictException(Messages.DeliveryOverRemaining);

        var fullyDelivered = order.Lines.All(line =>
            SalesOrderQuantities.Remaining(line.Qty, done.GetValueOrDefault(line.LineNo).Qty + thisDelivery.Where(q => q.LineNo == line.LineNo).Sum(q => q.Qty)) == 0m);
        return new SalesLinkedPosting(order, fullyDelivered);
    }

    public void AfterPost(LinkedPostingState? state, StockDocument document)
    {
        if (state is not SalesLinkedPosting { FullyDelivered: true, Order: var order }) return;
        var before = order.Status;
        order.CloseDelivered(current.UserId, document.PostedAt ?? clock.UtcNow); // the same moment as the posting itself
        audit.Record(AuditActions.SalesOrderClosed, AuditEntities.SalesOrder, order.No, new { Status = before },
            new { order.Status, order.CloseKind, Delivery = document.No });
    }

    private static OverDeliveryException OverDeliveryFor(SalesOrder order, IReadOnlyList<OverDelivery> over)
    {
        var lines = over.Select(o => (Over: o, Line: order.Lines.First(l => l.LineNo == o.LineNo))).ToList();
        var message = string.Join(" ", lines.Select(x => Messages.DeliveryLineOverDelivery(x.Over, x.Line.Product.Sku, x.Line.Uom.Code)));
        return new OverDeliveryException(message, lines.Select(x => new OverDeliveryLine(x.Over.LineNo, x.Line.Product.Sku, x.Over.Ordered, x.Over.Delivered, x.Over.Remaining, x.Over.Requested)).ToList());
    }
}
