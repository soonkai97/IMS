using Ims.Application.Features.Documents;
using Ims.Domain;
namespace Ims.Application.Features.Sales;

public class SalesOrderReader(IReadRepository<SalesOrder> orders, IReadRepository<User> users, IReadRepository<StockDocument> documents, IDeliveredQuantityReader delivered, ISalesOrderAccess access, IApprovalEvaluator approvals, ISystemSettings settings,
    IDocumentVersion version) : ISalesOrderReader, IAppService
{
    public async Task<SalesOrderDto> GetAsync(Guid publicId, CancellationToken ct)
    {
        // The caller's warehouses are part of the query: an order elsewhere is simply not found (404, like an unknown id).
        var dto = await orders.FirstOrDefaultAsync(new SalesOrderDtoSpec(publicId, access.WarehouseScope()), ct) ?? throw new NotFoundException(Messages.SalesOrderNotFound);

        // An order held for approval: say why (the thresholds it reaches) and whether the caller's tier can satisfy the rules in force. The amount is complete here: it is withheld from
        // a caller without the permission only when the response leaves the application, so a trigger carries it as the document's does.
        var amount = dto.TotalAmount.GetValueOrDefault();
        var heldForApproval = dto.RequiresApproval && dto.Status is SalesOrderStatus.Submitted or SalesOrderStatus.Approved;
        var triggers = heldForApproval
            ? (await approvals.TriggersAsync(DocumentType.SalesOrder, dto.TotalQty, amount, ct)).Select(ApprovalTriggerDto.From).Select(HideAmounts).ToList()
            : [];
        var withinLimit = dto.Status != SalesOrderStatus.Submitted || !dto.RequiresApproval ||
                          await approvals.ReviewRefusalAsync(DocumentType.SalesOrder, dto.IsLarge, dto.TotalQty, amount, ct) is null;
        var named = await WithDeliveriesAsync(await NamedAsync(dto, ct), ct);
        return named with { ApprovalTriggers = triggers, AllowedActions = access.AllowedActions(named, await settings.GetAsync(ct), withinLimit), ConcurrencyToken = version.Token(dto.Id, dto.RowVersion) };
    }

    /// <summary>
    /// What has been delivered per line (Posted deliveries only) and the deliveries themselves, computed now and never stored. A Draft or Submitted order cannot have a delivery (one is made from an Approved
    /// order), so those two reads are skipped (everything is still to deliver) and cost nothing there. No money: Warehouse staff see the quantities.
    /// </summary>
    private async Task<SalesOrderDto> WithDeliveriesAsync(SalesOrderDto dto, CancellationToken ct)
    {
        if (dto.Status is SalesOrderStatus.Draft or SalesOrderStatus.Submitted) return dto with { Lines = dto.Lines.Select(l => l with { RemainingQty = l.Qty, RemainingBaseQty = l.BaseQty }).ToList() };
        var done = await delivered.PostedByLineAsync(dto.Key, ct);
        var lines = dto.Lines.Select(l =>
        {
            var d = done.GetValueOrDefault(l.LineNo);
            return l with
            {
                DeliveredQty = d.Qty,
                RemainingQty = SalesOrderQuantities.Remaining(l.Qty, d.Qty),
                DeliveredBaseQty = d.BaseQty,
                RemainingBaseQty = SalesOrderQuantities.Remaining(l.BaseQty, d.BaseQty)
            };
        }).ToList();
        return dto with { Lines = lines, Deliveries = await documents.ListAsync(new DeliveriesOfOrderSpec(dto.Key), ct) };
    }

    /// <summary>
    /// The numbers of an amount rule are the order total at selling price (the threshold tells the caller where the total sits), so a caller without <c>so.manage</c> gets them as null whatever else they
    /// hold: cost.view alone, which the shared trigger contract is tagged with, must not reveal a selling total. A quantity rule is not money and stays.
    /// </summary>
    private ApprovalTriggerDto HideAmounts(ApprovalTriggerDto trigger) =>
        trigger.IsAmount && !access.MaySeeAmounts() ? trigger with { Threshold = null, Actual = null } : trigger;

    /// <summary>The people named on the order, in one query on their integer keys (the order has foreign keys to users but no navigation to them).</summary>
    private async Task<SalesOrderDto> NamedAsync(SalesOrderDto dto, CancellationToken ct)
    {
        var keys = new[] { dto.SubmittedByKey, dto.ApprovedByKey, dto.ReturnedByKey }.OfType<int>().Distinct().ToList();
        if (keys.Count == 0) return dto;
        var people = (await users.ListAsync(new UsersByKeysSpec(keys), ct)).ToDictionary(u => u.Key);
        UserLabel? Find(int? key) => key is { } k ? people.GetValueOrDefault(k) : null;
        return dto with
        {
            SubmittedById = Find(dto.SubmittedByKey)?.PublicId,
            SubmittedBy = Find(dto.SubmittedByKey)?.Username,
            SubmittedByName = Find(dto.SubmittedByKey)?.DisplayName,
            ApprovedBy = Find(dto.ApprovedByKey)?.Username,
            ApprovedByName = Find(dto.ApprovedByKey)?.DisplayName,
            ReturnedById = Find(dto.ReturnedByKey)?.PublicId,
            ReturnedBy = Find(dto.ReturnedByKey)?.Username,
            ReturnedByName = Find(dto.ReturnedByKey)?.DisplayName
        };
    }
}
