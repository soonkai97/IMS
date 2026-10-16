using Ims.Domain;
namespace Ims.Application.Features.Documents;

public class RecallDocumentHandler(IRepository<StockDocument> documents, IDocumentAccess access, IDocumentReader reader, IDocumentVersion version, IUnitOfWork uow, IAuditTrail audit)
    : ICommandHandler<RecallDocumentCommand, DocumentDto>
{
    public async Task<DocumentDto> HandleAsync(RecallDocumentCommand cmd, CancellationToken ct)
    {
        var doc = await documents.FirstOrDefaultAsync(new DocumentForUpdateSpec(cmd.Id), ct) ?? throw new NotFoundException(Messages.DocumentNotFound);
        access.EnsureDocumentInScope(doc.WarehouseId);

        access.EnsureMayRecall(doc);

        // A repeat of a recall that already succeeded (a double tap, a retry) answers with the document as it is, before the version is compared:
        // the retry holds the old token, and nothing is written, so there is no second audit row.
        if (access.IsRepeatedRecall(doc)) return await reader.GetAsync(cmd.Id, ct);

        version.EnsureCurrent(doc, cmd.IfMatch);
        var before = new { doc.Status, doc.RequiresApproval };
        doc.Recall();
        audit.Record(AuditActions.DocumentRecalled, AuditEntities.StockDocument, doc.No, before, new { doc.Status });
        await uow.SaveChangesAsync(ct);
        return await reader.GetAsync(cmd.Id, ct);
    }
}
