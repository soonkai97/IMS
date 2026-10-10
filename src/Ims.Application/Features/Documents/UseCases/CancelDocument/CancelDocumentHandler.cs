using Ims.Domain;
namespace Ims.Application.Features.Documents;

public class CancelDocumentHandler(IRepository<StockDocument> documents, IDocumentAccess access, IDocumentReader reader, IUnitOfWork uow, IAuditTrail audit)
    : ICommandHandler<CancelDocumentCommand, DocumentDto>
{
    public async Task<DocumentDto> HandleAsync(CancelDocumentCommand cmd, CancellationToken ct)
    {
        var doc = await documents.FirstOrDefaultAsync(new DocumentForUpdateSpec(cmd.Id), ct) ?? throw new NotFoundException(Messages.DocumentNotFound);
        access.EnsureWarehouse(doc.WarehouseId);
        var before = doc.Status;
        doc.Cancel(cmd.Data.Reason);
        audit.Record(AuditActions.DocumentCancelled, AuditEntities.StockDocument, doc.No, new { Status = before }, new { doc.Status, cmd.Data.Reason });
        await uow.SaveChangesAsync(ct);
        return await reader.GetAsync(cmd.Id, ct);
    }
}
