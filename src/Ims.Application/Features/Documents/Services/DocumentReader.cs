using Ims.Application.Features.Modules;
using Ims.Application.Features.Returns;
using Ims.Domain;
namespace Ims.Application.Features.Documents;

public class DocumentReader(IReadRepository<StockDocument> documents, IDocumentAccess access, ISystemSettings settings, IStockAvailability availability,
    IApprovalEvaluator approvals, IDocumentVersion version, IModuleSwitches modules, ICallerPermissions caller, IReturnedQuantityReader returned) : IDocumentReader, IAppService
{
    public async Task<DocumentDto> GetAsync(Guid publicId, CancellationToken ct)
    {
        // The caller's warehouses are part of the query: a document elsewhere is simply not found (404, like an unknown id).
        var dto = await documents.FirstOrDefaultAsync(new DocumentDtoSpec(publicId, access.WarehouseScope()), ct) ?? throw new NotFoundException(Messages.DocumentNotFound);
        dto = await WithStockAsync(dto, ct);
        var isReturn = dto.ReturnOf is not null; // taken before the link is hidden from a caller who may not see it: the approval rules of a return do not depend on who looks
        // The module is asked only for a linked receipt, so every other document costs nothing extra. The actions are decided with the link as the API sees it, before it is hidden from this caller.
        LinkedReceiptContext? link = dto.PurchaseOrder is not null ? new LinkedReceiptContext(await modules.IsEnabledAsync(ModuleCodes.Purchasing, ct), DocumentLinkKind.PurchaseOrder)
            : dto.SalesOrder is not null ? new LinkedReceiptContext(await modules.IsEnabledAsync(ModuleCodes.Sales, ct), DocumentLinkKind.SalesOrder)
            : dto.ReturnOf is not null ? new LinkedReceiptContext(await modules.IsEnabledAsync(ModuleCodes.Returns, ct), DocumentLinkKind.Return)
            : null;
        dto = WithoutLinkUnlessVisible(dto, link);
        dto = await WithReturnedQuantitiesAsync(dto, isReturn, ct);

        // A document held for approval: say why (the thresholds it reaches) and whether the caller's role can satisfy the rules in force.
        // The amount is complete here: it is hidden from a caller without cost.view only when the response leaves the application (CostVisibilityBehavior).
        var amount = dto.TotalAmount.GetValueOrDefault();
        var heldForApproval = dto.RequiresApproval && dto.Status is DocumentStatus.Submitted or DocumentStatus.Approved;
        var triggers = heldForApproval
            ? (await approvals.TriggersAsync(dto.Type, dto.TotalQty, amount, ct, isReturn)).Select(ApprovalTriggerDto.From).ToList()
            : [];
        var withinLimit = dto.Status != DocumentStatus.Submitted || !dto.RequiresApproval ||
                          await approvals.ReviewRefusalAsync(dto.Type, dto.IsLarge, dto.TotalQty, amount, ct, isReturn) is null;
        return dto with { ApprovalTriggers = triggers, AllowedActions = access.AllowedActions(dto, await ForReviewAsync(isReturn, ct), withinLimit, link), ConcurrencyToken = version.Token(dto.Id, dto.RowVersion) };
    }

    /// <summary>The settings the actions are decided with: a return is never self-approved, whatever the installation allows.</summary>
    private async Task<SystemSettings> ForReviewAsync(bool isReturn, CancellationToken ct)
    {
        var system = await settings.GetAsync(ct);
        return isReturn ? system with { AllowSelfApproval = false } : system;
    }

    /// <summary>
    /// A document's link to its order (its number and the line numbers) is shown only while the owning module is on AND to a caller who may read that kind of order (<c>po.read</c> for a receipt from a purchase
    /// order, <c>so.read</c> for a delivery from a sales order): the number and the order's existence are the order's business.
    /// </summary>
    private DocumentDto WithoutLinkUnlessVisible(DocumentDto dto, LinkedReceiptContext? link)
    {
        if (link is not { } linked) return dto;
        if (linked.Kind == DocumentLinkKind.Return)
            return caller.Has(Permissions.ReturnsCreate) && linked.ModuleOn ? dto : dto with { ReturnOf = null, Lines = dto.Lines.Select(l => l with { ReturnOfLineNo = null }).ToList() };
        if (linked.Kind == DocumentLinkKind.SalesOrder)
            return caller.Has(Permissions.SoRead) && linked.ModuleOn ? dto : dto with { SalesOrder = null, Lines = dto.Lines.Select(l => l with { SalesOrderLineNo = null }).ToList() };
        return caller.Has(Permissions.PoRead) && linked.ModuleOn ? dto : dto with { PurchaseOrder = null, Lines = dto.Lines.Select(l => l with { PurchaseOrderLineNo = null }).ToList() };
    }

    /// <summary>
    /// What Posted returns have taken back of each line of a Posted sale issue or receipt, for a caller who may create returns (the permission is offered only while the Returns module is on): <c>returnedQty</c> and <c>returnableQty</c> per line, computed now and
    /// never stored. The lines are in position order, which is the number a return names. Any other document, and any other caller, costs no query at all.
    /// </summary>
    private async Task<DocumentDto> WithReturnedQuantitiesAsync(DocumentDto dto, bool isReturn, CancellationToken ct)
    {
        var eligible = dto.Status == DocumentStatus.Posted && !isReturn && (dto.Type == DocumentType.Receipt || dto.Purpose == IssuePurpose.Sale);
        if (!eligible || !caller.Has(Permissions.ReturnsCreate) || !(await settings.GetAsync(ct)).IsModuleOn(ModuleCodes.Returns)) return dto; // the module state comes with the settings rows the reader reads anyway: no query of its own (IModuleSwitches would add one to every Posted receipt or issue read, which the statement pins forbid). DbSystemSettings compares the Module.* keys without case, like ModuleSwitches. Off: the figures are omitted
        var done = dto.HasReturns ? await returned.PostedByLineAsync(dto.Key, ct) : new Dictionary<int, ReturnedQuantity>(); // nothing has been returned: nothing to sum
        var lines = dto.Lines.Select((l, index) =>
        {
            var back = done.GetValueOrDefault(index + 1);
            return l with { ReturnedQty = back.Qty, ReturnableQty = ReturnQuantities.Remaining(l.Qty, back.Qty) };
        }).ToList();
        return dto with { Lines = lines };
    }

    /// <summary>Fills <c>available</c> on every line with one balance query for the whole document (no query per line).</summary>
    private async Task<DocumentDto> WithStockAsync(DocumentDto dto, CancellationToken ct)
    {
        if (!DocumentStatuses.IsOpen(dto.Status) || dto.Lines.Count == 0) return dto;
        var onHand = await availability.OnHandAsync(dto.WarehouseKey, dto.Lines.Select(l => l.ProductKey).Distinct().ToList(), ct);
        return dto with { Lines = dto.Lines.Select(l => l with { Available = onHand.GetValueOrDefault(l.ProductKey) }).ToList() };
    }
}
