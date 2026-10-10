using Ims.Domain;
namespace Ims.Application.Features.Documents;

public class DocumentReader(IReadRepository<StockDocument> documents, IDocumentAccess access, ISystemSettings settings, IStockAvailability availability,
    IApprovalEvaluator approvals) : IDocumentReader, IAppService
{
    public async Task<DocumentDto> GetAsync(int id, CancellationToken ct)
    {
        var dto = await documents.FirstOrDefaultAsync(new DocumentDtoSpec(id), ct) ?? throw new NotFoundException(Messages.DocumentNotFound);
        access.EnsureDocumentInScope(dto.WarehouseId);
        dto = await WithStockAsync(dto, ct);

        // A document held for approval: say why (the thresholds it reaches) and whether the caller's role can satisfy the rules in force.
        var heldForApproval = dto.RequiresApproval && dto.Status is DocumentStatus.Submitted or DocumentStatus.Approved;
        var triggers = heldForApproval ? await approvals.TriggersAsync(dto.Type, dto.TotalQty, dto.TotalAmount, ct) : [];
        var withinLimit = dto.Status != DocumentStatus.Submitted || !dto.RequiresApproval ||
                          await approvals.ReviewRefusalAsync(dto.Type, dto.IsLarge, dto.TotalQty, dto.TotalAmount, ct) is null;
        return dto with { ApprovalTriggers = triggers, AllowedActions = access.AllowedActions(dto, await settings.GetAsync(ct), withinLimit) };
    }

    /// <summary>Fills <c>available</c> on every line with one balance query for the whole document (no query per line).</summary>
    private async Task<DocumentDto> WithStockAsync(DocumentDto dto, CancellationToken ct)
    {
        if (!DocumentStatuses.IsOpen(dto.Status) || dto.Lines.Count == 0) return dto;
        var onHand = await availability.OnHandAsync(dto.WarehouseId, dto.Lines.Select(l => l.ProductId).Distinct().ToList(), ct);
        return dto with { Lines = dto.Lines.Select(l => l with { Available = onHand.GetValueOrDefault(l.ProductId) }).ToList() };
    }
}
