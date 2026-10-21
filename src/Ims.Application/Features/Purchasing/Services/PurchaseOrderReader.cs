using Ims.Domain;
using Ims.Application.Features.Documents;
namespace Ims.Application.Features.Purchasing;

public class PurchaseOrderReader(IReadRepository<PurchaseOrder> orders, IReadRepository<User> users, IPurchaseOrderAccess access, IPurchaseOrderReceiving receiving, IApprovalEvaluator approvals, ISystemSettings settings,
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
        var named = await WithReceivingAsync(await NamedAsync(dto, ct), ct);
        return named with { ApprovalTriggers = triggers, AllowedActions = access.AllowedActions(dto, await settings.GetAsync(ct), withinLimit), ConcurrencyToken = version.Token(dto.Id, dto.RowVersion) };
    }

    /// <summary>
    /// What has been received per line (Posted receipts only) and the receipts themselves, computed now and never stored. A Draft or Submitted order cannot have a receipt (one is made from an
    /// Approved order), so those two reads are skipped (everything is still to receive) and cost nothing there. No money: Warehouse staff see the quantities.
    /// </summary>
    private async Task<PurchaseOrderDto> WithReceivingAsync(PurchaseOrderDto dto, CancellationToken ct)
    {
        if (dto.Status is PurchaseOrderStatus.Draft or PurchaseOrderStatus.Submitted) return dto with { Lines = dto.Lines.Select(l => l with { RemainingQty = l.Qty, RemainingBaseQty = l.BaseQty }).ToList() };
        var received = await receiving.ReceivedAsync(dto.Key, ct);
        var lines = dto.Lines.Select(l =>
        {
            var done = received.GetValueOrDefault(l.LineNo);
            return l with
            {
                ReceivedQty = done.Qty, RemainingQty = PurchaseOrderQuantities.Remaining(l.Qty, done.Qty),
                ReceivedBaseQty = done.BaseQty, RemainingBaseQty = PurchaseOrderQuantities.Remaining(l.BaseQty, done.BaseQty)
            };
        }).ToList();
        return dto with { Lines = lines, Receipts = await receiving.ReceiptsAsync(dto.Key, ct) };
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
