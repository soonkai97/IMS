using Ims.Domain;
namespace Ims.Application.Features.Documents;

public class SubmitDocumentHandler(IRepository<StockDocument> documents, IApprovalEvaluator approvals,
    IStockAvailability availability, ISystemSettings settings, IDocumentAccess access, IDocumentReader reader,
    ICurrentUser current, IClock clock, IUnitOfWork uow, IAuditTrail audit) : ICommandHandler<SubmitDocumentCommand, DocumentDto>
{
    public async Task<DocumentDto> HandleAsync(SubmitDocumentCommand cmd, CancellationToken ct)
    {
        var doc = await documents.FirstOrDefaultAsync(new DocumentForUpdateSpec(cmd.Id), ct) ?? throw new NotFoundException(Messages.DocumentNotFound);
        access.EnsureWarehouse(doc.WarehouseId);
        DocumentVersion.EnsureCurrent(doc, cmd.IfMatch);

        // Submission is blocked up front when stock is short, unless negative stock is allowed (OUT-02); then the shortfall is flagged
        // so a supervisor knows an override is coming.
        var requiresOverride = false;
        if (doc.Type == DocumentType.Issue)
        {
            if ((await settings.GetAsync(ct)).AllowNegativeStock) requiresOverride = await availability.IsShortAsync(doc, ct);
            else await availability.EnsureAvailableAsync(doc, ct);
        }

        var assessment = await approvals.AssessAsync(doc, ct);
        var before = doc.Status;
        doc.Submit(current.UserId, assessment.Required, clock.UtcNow, assessment.IsLarge, requiresOverride);
        audit.Record(AuditActions.DocumentSubmitted, AuditEntities.StockDocument, doc.No, new { Status = before },
            new { doc.Status, doc.RequiresApproval, doc.IsLarge, doc.RequiresOverride });
        await uow.SaveChangesAsync(ct);
        return await reader.GetAsync(cmd.Id, ct);
    }
}
