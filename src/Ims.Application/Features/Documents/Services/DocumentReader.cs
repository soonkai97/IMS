using Ims.Domain;
namespace Ims.Application.Features.Documents;

public class DocumentReader(IReadRepository<StockDocument> documents, IDocumentAccess access, ISystemSettings settings, IStockAvailability availability,
    IApprovalEvaluator approvals) : IDocumentReader, IAppService
{
    /// <summary>Statuses in which "on hand now" still says something about the document; afterwards <c>available</c> is left empty.</summary>
    private static readonly DocumentStatus[] OpenStatuses = [DocumentStatus.Draft, DocumentStatus.Submitted, DocumentStatus.Approved];

    public async Task<DocumentDto> GetAsync(int id, CancellationToken ct)
    {
        var dto = await documents.FirstOrDefaultAsync(new DocumentDtoSpec(id), ct) ?? throw new NotFoundException(Messages.DocumentNotFound);
        access.EnsureDocumentInScope(dto.WarehouseId);

        // A document held for approval: whether the caller's role can satisfy the approval rules in force (they may have changed since it was submitted).
        var withinLimit = dto.Status != DocumentStatus.Submitted || !dto.RequiresApproval ||
                          await approvals.ReviewRefusalAsync(dto.Type, dto.IsLarge, dto.TotalQty, dto.TotalAmount, ct) is null;
        return (await WithStockAsync(dto, ct)) with { AllowedActions = access.AllowedActions(dto, await settings.GetAsync(ct), withinLimit) };
    }

    /// <summary>Fills <c>available</c> on every line with one balance query for the whole document (no query per line).</summary>
    private async Task<DocumentDto> WithStockAsync(DocumentDto dto, CancellationToken ct)
    {
        if (!OpenStatuses.Contains(dto.Status) || dto.Lines.Count == 0) return dto;
        var onHand = await availability.OnHandAsync(dto.WarehouseId, dto.Lines.Select(l => l.ProductId).Distinct().ToList(), ct);
        return dto with { Lines = dto.Lines.Select(l => l with { Available = onHand.GetValueOrDefault(l.ProductId) }).ToList() };
    }
}
