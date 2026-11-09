using Ims.Domain;
namespace Ims.Application.Features.Documents;

/// <summary>BR-02: a reversal writes opposite ledger entries linked to the original document; nothing is edited or deleted.</summary>
public class ReverseDocumentHandler(IRepository<StockDocument> documents, IRepository<StockLedgerEntry> ledger, IReadRepository<Product> products,
    IBalanceLoader balances, IPostingEngine engine, ISystemSettings settings, IDocumentAccess access, IDocumentReader reader, IDocumentVersion version,
    ITransactionLocker locker, ICurrentUser current, IClock clock, IUnitOfWork uow, IAuditTrail audit) : ICommandHandler<ReverseDocumentCommand, DocumentDto>
{
    public async Task<DocumentDto> HandleAsync(ReverseDocumentCommand cmd, CancellationToken ct)
    {
        await uow.ExecuteAsync(async () =>
        {
            var doc = await documents.FirstOrDefaultAsync(new DocumentForUpdateSpec(cmd.Id), ct) ?? throw new NotFoundException(Messages.DocumentNotFound);
            access.EnsureDocumentInScope(doc.WarehouseId);
            version.EnsureCurrent(doc, cmd.IfMatch);
            // A delivery created from a sales order changes what the order has delivered: the order's lock comes FIRST, before any stock lock, exactly where posting takes it, so a reversal cannot
            // slip between a posting's read of the delivered quantities and the auto-close that depends on them, nor under a close short's check that something was delivered.
            if (doc.SalesOrderId is { } orderKey) await locker.LockSalesOrderAsync(orderKey, ct);
            // The reversal of a return changes what can still be returned of its original, so it takes the original ret lock first, exactly where posting takes it. (Reversing an ORIGINAL that has Posted returns is not
            // refused yet: that needs a query on every reversal and the module switch may not be read here; it is the open item of PL3.17.)
            if (doc.ReturnOfDocumentId is { } originalKey) await locker.LockReturnOriginAsync(originalKey, ct);
            var now = clock.UtcNow;
            doc.MarkReversed(cmd.Data.Reason, now, current.UserId);

            var systemSettings = await settings.GetAsync(ct);
            var ctx = new PostingContext(doc, systemSettings, current.UserId, canOverrideNegative: false, now);
            var documentBalances = await balances.LoadForUpdateAsync(doc, ct);

            // Check the whole document before anything moves, so the answer names every product that would go negative, not just the first.
            var deficits = engine.FindReversalDeficits(doc, documentBalances, systemSettings);
            if (deficits.Count > 0) throw await BlockedAsync(doc, deficits, ct);

            engine.Reverse(doc, documentBalances, ctx);

            ledger.AddRange(ctx.Entries);
            audit.Record(AuditActions.DocumentReversed, AuditEntities.StockDocument, doc.No, new { Status = DocumentStatus.Posted }, new { doc.Status, Reason = doc.ReversalReason });
            await uow.SaveChangesAsync(ct);
            return doc.Id;
        }, ct);
        return await reader.GetAsync(cmd.Id, ct);
    }

    private async Task<ReversalBlockedException> BlockedAsync(StockDocument doc, IReadOnlyDictionary<int, decimal> deficits, CancellationToken ct)
    {
        var labels = (await products.ListAsync(new ProductLabelsSpec(deficits.Keys.ToList()), ct)).ToDictionary(p => p.Id);
        var shortages = deficits.Select(d => new ReversalShortage(labels[d.Key].PublicId, labels[d.Key].Sku, labels[d.Key].UomCode, d.Value)).ToList();
        return new ReversalBlockedException(doc.No, shortages);
    }
}
