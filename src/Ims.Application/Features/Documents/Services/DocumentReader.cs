using Ims.Domain;
namespace Ims.Application.Features.Documents;

public class DocumentReader(IReadRepository<StockDocument> documents, IDocumentAccess access, ISystemSettings settings, IStockAvailability availability,
    IApprovalEvaluator approvals) : IDocumentReader, IAppService
{
    public async Task<DocumentDto> GetAsync(int id, CancellationToken ct)
    {
        // The caller's warehouses are part of the query: a document elsewhere is simply not found (404, like an unknown id).
        var dto = await documents.FirstOrDefaultAsync(new DocumentDtoSpec(id, access.WarehouseScope()), ct) ?? throw new NotFoundException(Messages.DocumentNotFound);
        dto = await WithStockAsync(dto, ct);

        // A document held for approval: say why (the thresholds it reaches) and whether the caller's role can satisfy the rules in force.
        // The amount is complete here: it is hidden from a caller without cost.view only when the response leaves the application (CostVisibilityBehavior).
        var amount = dto.TotalAmount.GetValueOrDefault();
        var heldForApproval = dto.RequiresApproval && dto.Status is DocumentStatus.Submitted or DocumentStatus.Approved;
        var triggers = heldForApproval
            ? (await approvals.TriggersAsync(dto.Type, dto.TotalQty, amount, ct)).Select(ApprovalTriggerDto.From).ToList()
            : [];
        var withinLimit = dto.Status != DocumentStatus.Submitted || !dto.RequiresApproval ||
                          await approvals.ReviewRefusalAsync(dto.Type, dto.IsLarge, dto.TotalQty, amount, ct) is null;
        return dto with { ApprovalTriggers = triggers, AllowedActions = access.AllowedActions(dto, await settings.GetAsync(ct), withinLimit) };
    }

    /// <summary>Fills <c>available</c> on every line with one balance query for the whole document (no query per line).</summary>
    private async Task<DocumentDto> WithStockAsync(DocumentDto dto, CancellationToken ct)
    {
        if (!DocumentStatuses.IsOpen(dto.Status) || dto.Lines.Count == 0) return dto;
        var onHand = await availability.OnHandAsync(dto.WarehouseKey, dto.Lines.Select(l => l.ProductKey).Distinct().ToList(), ct);
        return dto with { Lines = dto.Lines.Select(l => l with { Available = onHand.GetValueOrDefault(l.ProductKey) }).ToList() };
    }
}
