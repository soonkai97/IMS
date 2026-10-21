using Ims.Domain;
using Ims.Application.Features.Documents;
namespace Ims.Application.Features.Purchasing;

public class PurchaseOrderReader(IReadRepository<PurchaseOrder> orders, IReadRepository<User> users, IPurchaseOrderAccess access, IApprovalEvaluator approvals, ISystemSettings settings,
    IDocumentVersion version) : IPurchaseOrderReader, IAppService
{
    public async Task<PurchaseOrderDto> GetAsync(Guid publicId, CancellationToken ct)
    {
        // The caller's warehouses are part of the query: an order elsewhere is simply not found (404, like an unknown id).
        var dto = await orders.FirstOrDefaultAsync(new PurchaseOrderDtoSpec(publicId, access.WarehouseScope()), ct) ?? throw new NotFoundException(Messages.PurchaseOrderNotFound);

        // An order held for approval: say why (the thresholds it reaches) and whether the caller's tier can satisfy the rules in force. The amount is complete here: it is hidden from
        // a caller without cost.view only when the response leaves the application (CostVisibilityBehavior), so a trigger carries it as the document's does.
        var amount = dto.TotalAmount.GetValueOrDefault();
        var heldForApproval = dto.RequiresApproval && dto.Status is PurchaseOrderStatus.Submitted or PurchaseOrderStatus.Approved;
        var triggers = heldForApproval
            ? (await approvals.TriggersAsync(DocumentType.PurchaseOrder, dto.TotalQty, amount, ct)).Select(ApprovalTriggerDto.From).ToList()
            : [];
        var withinLimit = dto.Status != PurchaseOrderStatus.Submitted || !dto.RequiresApproval ||
                          await approvals.ReviewRefusalAsync(DocumentType.PurchaseOrder, dto.IsLarge, dto.TotalQty, amount, ct) is null;
        var named = await NamedAsync(dto, ct);
        return named with { ApprovalTriggers = triggers, AllowedActions = access.AllowedActions(dto, await settings.GetAsync(ct), withinLimit), ConcurrencyToken = version.Token(dto.Id, dto.RowVersion) };
    }

    /// <summary>The people named on the order, in one query on their integer keys (the order has foreign keys to users but no navigation to them).</summary>
    private async Task<PurchaseOrderDto> NamedAsync(PurchaseOrderDto dto, CancellationToken ct)
    {
        var keys = new[] { dto.SubmittedByKey, dto.ApprovedByKey, dto.ReturnedByKey }.OfType<int>().Distinct().ToList();
        if (keys.Count == 0) return dto;
        var people = (await users.ListAsync(new UsersByKeysSpec(keys), ct)).ToDictionary(u => u.Key);
        UserLabel? Find(int? key) => key is { } k ? people.GetValueOrDefault(k) : null;
        return dto with
        {
            SubmittedById = Find(dto.SubmittedByKey)?.PublicId, SubmittedBy = Find(dto.SubmittedByKey)?.Username, SubmittedByName = Find(dto.SubmittedByKey)?.DisplayName,
            ApprovedBy = Find(dto.ApprovedByKey)?.Username, ApprovedByName = Find(dto.ApprovedByKey)?.DisplayName,
            ReturnedById = Find(dto.ReturnedByKey)?.PublicId, ReturnedBy = Find(dto.ReturnedByKey)?.Username, ReturnedByName = Find(dto.ReturnedByKey)?.DisplayName
        };
    }
}
