using Ims.Application.Features.Modules;
using Ims.Domain;
namespace Ims.Application.Features.Documents;

public class DocumentReader(IReadRepository<StockDocument> documents, IDocumentAccess access, ISystemSettings settings, IStockAvailability availability,
    IApprovalEvaluator approvals, IDocumentVersion version, IModuleSwitches modules, ICallerPermissions caller) : IDocumentReader, IAppService
{
    public async Task<DocumentDto> GetAsync(Guid publicId, CancellationToken ct)
    {
        // The caller's warehouses are part of the query: a document elsewhere is simply not found (404, like an unknown id).
        var dto = await documents.FirstOrDefaultAsync(new DocumentDtoSpec(publicId, access.WarehouseScope()), ct) ?? throw new NotFoundException(Messages.DocumentNotFound);
        dto = await WithStockAsync(dto, ct);
        // The module is asked only for a linked receipt, so every other document costs nothing extra. The actions are decided with the link as the API sees it, before it is hidden from this caller.
        LinkedReceiptContext? link = dto.PurchaseOrder is null ? null : new LinkedReceiptContext(await modules.IsEnabledAsync(ModuleCodes.Purchasing, ct));
        dto = WithoutLinkUnlessVisible(dto, link);

        // A document held for approval: say why (the thresholds it reaches) and whether the caller's role can satisfy the rules in force.
        // The amount is complete here: it is hidden from a caller without cost.view only when the response leaves the application (CostVisibilityBehavior).
        var amount = dto.TotalAmount.GetValueOrDefault();
        var heldForApproval = dto.RequiresApproval && dto.Status is DocumentStatus.Submitted or DocumentStatus.Approved;
        var triggers = heldForApproval
            ? (await approvals.TriggersAsync(dto.Type, dto.TotalQty, amount, ct)).Select(ApprovalTriggerDto.From).ToList()
            : [];
        var withinLimit = dto.Status != DocumentStatus.Submitted || !dto.RequiresApproval ||
                          await approvals.ReviewRefusalAsync(dto.Type, dto.IsLarge, dto.TotalQty, amount, ct) is null;
        return dto with { ApprovalTriggers = triggers, AllowedActions = access.AllowedActions(dto, await settings.GetAsync(ct), withinLimit, link), ConcurrencyToken = version.Token(dto.Id, dto.RowVersion) };
    }

    /// <summary>A receipt's link to its purchase order (its number and the line numbers) is shown only while the Purchasing module is on AND to a caller who may read purchase orders: the number and the order's existence are the order's business. </summary>
    private DocumentDto WithoutLinkUnlessVisible(DocumentDto dto, LinkedReceiptContext? link)
    {
        if (link is not { } linked || (caller.Has(Permissions.PoRead) && linked.PurchasingOn)) return dto;
        return dto with { PurchaseOrder = null, Lines = dto.Lines.Select(l => l with { PurchaseOrderLineNo = null }).ToList() };
    }

    /// <summary>Fills <c>available</c> on every line with one balance query for the whole document (no query per line).</summary>
    private async Task<DocumentDto> WithStockAsync(DocumentDto dto, CancellationToken ct)
    {
        if (!DocumentStatuses.IsOpen(dto.Status) || dto.Lines.Count == 0) return dto;
        var onHand = await availability.OnHandAsync(dto.WarehouseKey, dto.Lines.Select(l => l.ProductKey).Distinct().ToList(), ct);
        return dto with { Lines = dto.Lines.Select(l => l with { Available = onHand.GetValueOrDefault(l.ProductKey) }).ToList() };
    }
}
