using Ims.Domain;
namespace Ims.Application.Features.Purchasing;

/// <summary>
/// Writes the audit row of every change to a purchase order (DDD-12, docs/architecture/ddd-roadmap.md 2.4, 2.5; the twin of <c>SalesOrderAuditSubscriber</c>). It runs INSIDE the transaction of the use case, before the
/// unit of work saves, so the row is written by the same save as the change and commits or rolls back with it (AUTH-03). It reads ONLY the facts of the event (never an entity, never a <c>REC_*</c> column) and stages
/// the row through <see cref="IAuditTrail"/> as the handlers did before: the same action, entity type, entity id (the order number), the same Before and After keys in the same order, the same
/// <see cref="AuditFields"/> tag on the amount (so the scrubber still withholds it from a caller without <c>cost.view</c>), and the actor and the time are read from the current user and the clock at this moment, as
/// <c>AuditTrail.Record</c> does.
/// <para>
/// Not here: the row for an order that posting closes as fully received, which <c>LinkedReceiptPosting</c> still writes itself, in its place before the receipt's own Posted row (it moves with DDD-17). The event of
/// that close is skipped below, or the row would be written twice.
/// </para>
/// </summary>
public sealed class PurchaseOrderAuditSubscriber(IAuditTrail audit) : IAppService,
    IDomainEventHandler<PurchaseOrderCreated>, IDomainEventHandler<PurchaseOrderUpdated>, IDomainEventHandler<PurchaseOrderSubmitted>, IDomainEventHandler<PurchaseOrderApproved>,
    IDomainEventHandler<PurchaseOrderRejected>, IDomainEventHandler<PurchaseOrderRecalled>, IDomainEventHandler<PurchaseOrderCancelled>, IDomainEventHandler<PurchaseOrderClosed>
{
    public Task HandleAsync(PurchaseOrderCreated domainEvent, CancellationToken ct)
    {
        audit.Record(AuditActions.PurchaseOrderCreated, AuditEntities.PurchaseOrder, domainEvent.No, after: Summary(domainEvent.Summary));
        return Task.CompletedTask;
    }

    public Task HandleAsync(PurchaseOrderUpdated domainEvent, CancellationToken ct)
    {
        audit.Record(AuditActions.PurchaseOrderUpdated, AuditEntities.PurchaseOrder, domainEvent.No, Summary(domainEvent.Before), Summary(domainEvent.After));
        return Task.CompletedTask;
    }

    public Task HandleAsync(PurchaseOrderSubmitted domainEvent, CancellationToken ct)
    {
        audit.Record(AuditActions.PurchaseOrderSubmitted, AuditEntities.PurchaseOrder, domainEvent.No, new { Status = domainEvent.StatusBefore },
            new Dictionary<string, object?>
            {
                ["Status"] = domainEvent.StatusAfter,
                ["RequiresApproval"] = domainEvent.RequiresApproval,
                ["IsLarge"] = domainEvent.IsLarge,
                [AuditFields.Amount] = domainEvent.Amount.Value
            });
        return Task.CompletedTask;
    }

    public Task HandleAsync(PurchaseOrderApproved domainEvent, CancellationToken ct)
    {
        audit.Record(AuditActions.PurchaseOrderApproved, AuditEntities.PurchaseOrder, domainEvent.No, new { Status = domainEvent.StatusBefore }, new { Status = domainEvent.StatusAfter, domainEvent.IsLarge });
        return Task.CompletedTask;
    }

    public Task HandleAsync(PurchaseOrderRejected domainEvent, CancellationToken ct)
    {
        audit.Record(AuditActions.PurchaseOrderRejected, AuditEntities.PurchaseOrder, domainEvent.No, new { Status = domainEvent.StatusBefore }, new { Status = domainEvent.StatusAfter, domainEvent.Reason });
        return Task.CompletedTask;
    }

    public Task HandleAsync(PurchaseOrderRecalled domainEvent, CancellationToken ct)
    {
        audit.Record(AuditActions.PurchaseOrderRecalled, AuditEntities.PurchaseOrder, domainEvent.No, new { Status = domainEvent.StatusBefore, RequiresApproval = domainEvent.RequiresApprovalBefore }, new { Status = domainEvent.StatusAfter });
        return Task.CompletedTask;
    }

    public Task HandleAsync(PurchaseOrderCancelled domainEvent, CancellationToken ct)
    {
        audit.Record(AuditActions.PurchaseOrderCancelled, AuditEntities.PurchaseOrder, domainEvent.No, new { Status = domainEvent.StatusBefore }, new { Status = domainEvent.StatusAfter, domainEvent.Reason });
        return Task.CompletedTask;
    }

    public Task HandleAsync(PurchaseOrderClosed domainEvent, CancellationToken ct)
    {
        // Posting the last receipt closes the order as Received and LinkedReceiptPosting writes that row itself, in its place in the order of the rows (before the receipt's own Posted row). Until DDD-17 moves it
        // here, handling that event as well would write the row twice.
        if (domainEvent.Kind == PurchaseOrderCloseKind.Received) return Task.CompletedTask;

        audit.Record(AuditActions.PurchaseOrderClosed, AuditEntities.PurchaseOrder, domainEvent.No, new { Status = domainEvent.StatusBefore }, new { Status = domainEvent.StatusAfter, CloseKind = domainEvent.Kind, domainEvent.Reason });
        return Task.CompletedTask;
    }

    /// <summary>The payload of a create and of both sides of an update, keys in the order the handlers always wrote them.</summary>
    private static Dictionary<string, object?> Summary(PurchaseOrderSummary summary) => new()
    {
        ["Status"] = summary.Status,
        ["WarehouseId"] = summary.WarehouseId,
        ["SupplierId"] = summary.SupplierId,
        ["Lines"] = summary.LineCount,
        [AuditFields.Amount] = summary.Amount
    };
}
