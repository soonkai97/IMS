using Ims.Domain;
namespace Ims.Application.Features.Documents;

public class RejectDocumentHandler(IRepository<StockDocument> documents, IDocumentAccess access, IDocumentReader reader, IUnitOfWork uow, IAuditTrail audit)
    : ICommandHandler<RejectDocumentCommand, DocumentDto>
{
    public async Task<DocumentDto> HandleAsync(RejectDocumentCommand cmd, CancellationToken ct)
    {
        var doc = await documents.FirstOrDefaultAsync(new DocumentForUpdateSpec(cmd.Id), ct) ?? throw new NotFoundException(Messages.DocumentNotFound);
        access.EnsureWarehouse(doc.WarehouseId);
        var statusBefore = doc.Status;
        doc.Reject(cmd.Data.Reason);
        audit.Record(AuditActions.DocumentRejected, AuditEntities.StockDocument, doc.No, new { Status = statusBefore }, new { doc.Status, cmd.Data.Reason });
        await uow.SaveChangesAsync(ct);
        return await reader.GetAsync(cmd.Id, ct);
    }
}
