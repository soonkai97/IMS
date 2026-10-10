using Ims.Domain;
namespace Ims.Application.Features.Documents;

public class RejectDocumentHandler(IRepository<StockDocument> documents, IApprovalEvaluator approvals, ISystemSettings settings, IDocumentAccess access,
    IDocumentReader reader, ICurrentUser current, IClock clock, IUnitOfWork uow, IAuditTrail audit) : ICommandHandler<RejectDocumentCommand, DocumentDto>
{
    public async Task<DocumentDto> HandleAsync(RejectDocumentCommand cmd, CancellationToken ct)
    {
        var doc = await documents.FirstOrDefaultAsync(new DocumentForUpdateSpec(cmd.Id), ct) ?? throw new NotFoundException(Messages.DocumentNotFound);
        access.EnsureDocumentInScope(doc.WarehouseId);
        DocumentVersion.EnsureCurrent(doc, cmd.IfMatch);

        // Rejecting is a review like approving: only the reviewer the document is routed to may do it (a state error is still reported by the domain below).
        if (doc.Status == DocumentStatus.Submitted && doc.RequiresApproval &&
            await approvals.ReviewRefusalAsync(doc, ct) is { } refusal)
            throw new ForbiddenException(refusal);

        var statusBefore = doc.Status;
        doc.Reject(cmd.Data.Reason, current.UserId, (await settings.GetAsync(ct)).AllowSelfApproval, clock.UtcNow);
        audit.Record(AuditActions.DocumentRejected, AuditEntities.StockDocument, doc.No, new { Status = statusBefore }, new { doc.Status, Reason = doc.RejectionReason });
        await uow.SaveChangesAsync(ct);
        return await reader.GetAsync(cmd.Id, ct);
    }
}
