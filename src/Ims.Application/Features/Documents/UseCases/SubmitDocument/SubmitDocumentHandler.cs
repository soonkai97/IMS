using Ims.Application.Features.Purchasing;
using Ims.Application.Features.Returns;
using Ims.Application.Features.Sales;
using Ims.Domain;
namespace Ims.Application.Features.Documents;

public sealed class SubmitDocumentHandler(IRepository<StockDocument> documents, IApprovalEvaluator approvals, IIssueValuation valuation,
    IStockAvailability availability, IReceiptLinkRules links, ISalesDeliveryRules deliveries, IReturnRules returns, ISystemSettings settings, IDocumentAccess access, IDocumentReader reader, IDocumentVersion version,
    ICurrentUser current, IClock clock, IUnitOfWork uow, IAuditTrail audit) : ICommandHandler<SubmitDocumentCommand, DocumentDto>
{
    public async Task<DocumentDto> HandleAsync(SubmitDocumentCommand cmd, CancellationToken ct)
    {
        var doc = await documents.FirstOrDefaultAsync(new DocumentForUpdateSpec(cmd.Id), ct) ?? throw new NotFoundException(Messages.DocumentNotFound);
        access.EnsureDocumentInScope(doc.WarehouseId);
        access.EnsureMayEditOrSubmit(doc);
        version.EnsureCurrent(doc, cmd.IfMatch);
        await links.EnsureCanSubmitAsync(doc, ct); // a receipt from a purchase order is checked against what remains; any other document is not touched
        await deliveries.EnsureCanSubmitAsync(doc, ct); // and a delivery from a sales order the same way
        await returns.EnsureCanSubmitAsync(doc, ct); // and a return of a posted document

        // Submission is blocked up front when stock is short, unless negative stock is allowed (OUT-02); then the shortfall is flagged
        // so it is sent to an approver, who gives the override reason when approving.
        var requiresOverride = StockAvailabilityPolicy.Covers(doc) && await availability.RequiresOverrideAsync(doc, (await settings.GetAsync(ct)).AllowNegativeStock, ct);

        // An issue is valued at the average cost of this moment; that value is what the rules are tested against and what the approver sees.
        await valuation.EstimateAsync(doc, ct);
        var assessment = await approvals.AssessAsync(doc, ct);
        var before = doc.Status;
        // An override always goes to an approver: the approver is the one who gives the reason, whatever the value thresholds say.
        doc.Submit(current.UserId, StockAvailabilityPolicy.RequiresApproval(assessment.Required, requiresOverride), clock.UtcNow, assessment.IsLarge, requiresOverride);
        audit.Record(AuditActions.DocumentSubmitted, AuditEntities.StockDocument, doc.No, new { Status = before },
            new Dictionary<string, object?>
            {
                [nameof(doc.Status)] = doc.Status,
                [nameof(doc.RequiresApproval)] = doc.RequiresApproval,
                [nameof(doc.IsLarge)] = doc.IsLarge,
                [nameof(doc.RequiresOverride)] = doc.RequiresOverride,
                [AuditFields.Amount] = assessment.Amount
            });
        await uow.SaveChangesAsync(ct);
        return await reader.GetAsync(cmd.Id, ct);
    }
}
