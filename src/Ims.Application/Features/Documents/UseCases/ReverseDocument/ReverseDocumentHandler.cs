using Ims.Domain;
namespace Ims.Application.Features.Documents;

/// <summary>BR-02: a reversal writes opposite ledger entries linked to the original document; nothing is edited or deleted.</summary>
public class ReverseDocumentHandler(IRepository<StockDocument> documents, IRepository<StockLedgerEntry> ledger, IBalanceLoader balances,
    IPostingEngine engine, ISystemSettings settings, IDocumentAccess access, IDocumentReader reader,
    ICurrentUser current, IClock clock, IUnitOfWork uow, IAuditTrail audit) : ICommandHandler<ReverseDocumentCommand, DocumentDto>
{
    public async Task<DocumentDto> HandleAsync(ReverseDocumentCommand cmd, CancellationToken ct)
    {
        await uow.ExecuteAsync(async () =>
        {
            var doc = await documents.FirstOrDefaultAsync(new DocumentForUpdateSpec(cmd.Id), ct) ?? throw new NotFoundException(Messages.DocumentNotFound);
            access.EnsureWarehouse(doc.WarehouseId);
            doc.MarkReversed(cmd.Data.Reason);

            var ctx = new PostingContext(doc, await settings.GetAsync(ct), current.UserId, canOverrideNegative: false, null, clock.UtcNow);
            engine.Reverse(doc, await balances.LoadForUpdateAsync(doc, ct), ctx);

            ledger.AddRange(ctx.Entries);
            audit.Record(AuditActions.DocumentReversed, AuditEntities.StockDocument, doc.No, new { Status = DocumentStatus.Posted }, new { doc.Status, cmd.Data.Reason });
            await uow.SaveChangesAsync(ct);
            return doc.Id;
        }, ct);
        return await reader.GetAsync(cmd.Id, ct);
    }
}
