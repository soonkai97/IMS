using Ims.Domain;
namespace Ims.Application.Features.Documents;

public class ApproveDocumentHandler(IRepository<StockDocument> documents, IApprovalEvaluator approvals, ISystemSettings settings,
    IDocumentAccess access, IDocumentReader reader, ICurrentUser current, IClock clock, IUnitOfWork uow, IAuditTrail audit)
    : ICommandHandler<ApproveDocumentCommand, DocumentDto>
{
    public async Task<DocumentDto> HandleAsync(ApproveDocumentCommand cmd, CancellationToken ct)
    {
        var doc = await documents.FirstOrDefaultAsync(new DocumentForUpdateSpec(cmd.Id), ct) ?? throw new NotFoundException(Messages.DocumentNotFound);
        access.EnsureDocumentInScope(doc.WarehouseId);
        DocumentVersion.EnsureCurrent(doc, cmd.IfMatch);

        if (await approvals.ReviewRefusalAsync(doc, ct) is { } refusal)
            throw new ForbiddenException(refusal);

        doc.Approve(current.UserId, (await settings.GetAsync(ct)).AllowSelfApproval, clock.UtcNow, cmd.Data.NegativeStockOverrideReason);
        audit.Record(AuditActions.DocumentApproved, AuditEntities.StockDocument, doc.No, new { Status = DocumentStatus.Submitted }, new { doc.Status, doc.NegativeStockOverrideReason });
        await uow.SaveChangesAsync(ct);
        return await reader.GetAsync(cmd.Id, ct);
    }
}
