using Ims.Application.Features.Purchasing;
using Ims.Domain;
namespace Ims.Application.Features.Documents;

/// <summary>Posting: writes the ledger and updates balances in one transaction, retried on concurrent changes (BR-01, BR-12).</summary>
public class PostDocumentHandler(IRepository<StockDocument> documents, IRepository<StockLedgerEntry> ledger, IBalanceLoader balances,
    IPostingEngine engine, ILinkedReceiptPosting linked, ISystemSettings settings, IDocumentAccess access, IDocumentReader reader, IDocumentVersion version,
    ICurrentUser current, IClock clock, IUnitOfWork uow, IAuditTrail audit) : ICommandHandler<PostDocumentCommand, DocumentDto>
{
    public async Task<DocumentDto> HandleAsync(PostDocumentCommand cmd, CancellationToken ct)
    {
        await uow.ExecuteAsync(async () =>
        {
            var doc = await documents.FirstOrDefaultAsync(new DocumentForUpdateSpec(cmd.Id), ct) ?? throw new NotFoundException(Messages.DocumentNotFound);
            access.EnsureDocumentInScope(doc.WarehouseId);
            version.EnsureCurrent(doc, cmd.IfMatch);
            var rights = current.Rights();
            if (doc.RequiresApproval && !PostingAuthority.MayPostApprovalRequiredDocument(rights))
                throw new ForbiddenException(Messages.NeedsApprovalNotForStaff);
            doc.EnsureCanPost();
            var statusBefore = doc.Status;

            // A receipt from a purchase order takes the order's lock first (before any stock lock) and is checked against what remains; any other document is not touched here.
            var linkedPosting = await linked.BeforePostAsync(doc, ct);
            var ctx = new PostingContext(doc, await settings.GetAsync(ct), current.UserId,
                canOverrideNegative: PostingAuthority.MayOverrideNegativeStock(rights), clock.UtcNow);
            engine.Post(doc, await balances.LoadForUpdateAsync(doc, ct), ctx);

            ledger.AddRange(ctx.Entries);
            linked.AfterPost(linkedPosting, doc);
            audit.Record(AuditActions.DocumentPosted, AuditEntities.StockDocument, doc.No, new { Status = statusBefore }, new { doc.Status, ctx.OverrideUsed });
            await uow.SaveChangesAsync(ct);
            return doc.Id;
        }, ct);
        return await reader.GetAsync(cmd.Id, ct);
    }
}
