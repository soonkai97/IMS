using Ims.Domain;
namespace Ims.Application.Features.Documents;

/// <summary>BR-02: a reversal writes opposite ledger entries linked to the original document; nothing is edited or deleted.</summary>
public class ReverseDocumentHandler(IRepository<StockDocument> documents, IRepository<StockLedgerEntry> ledger, IReadRepository<Product> products,
    IBalanceLoader balances, IPostingEngine engine, ISystemSettings settings, IDocumentAccess access, IDocumentReader reader,
    ICurrentUser current, IClock clock, IUnitOfWork uow, IAuditTrail audit) : ICommandHandler<ReverseDocumentCommand, DocumentDto>
{
    public async Task<DocumentDto> HandleAsync(ReverseDocumentCommand cmd, CancellationToken ct)
    {
        await uow.ExecuteAsync(async () =>
        {
            var doc = await documents.FirstOrDefaultAsync(new DocumentForUpdateSpec(cmd.Id), ct) ?? throw new NotFoundException(Messages.DocumentNotFound);
            access.EnsureDocumentInScope(doc.WarehouseId);
            DocumentVersion.EnsureCurrent(doc, cmd.IfMatch);
            var now = clock.UtcNow;
            doc.MarkReversed(cmd.Data.Reason, now);

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
        var shortages = deficits.Select(d => new ReversalShortage(d.Key, labels[d.Key].Sku, labels[d.Key].UomCode, d.Value)).ToList();
        return new ReversalBlockedException(doc.No, shortages);
    }
}
