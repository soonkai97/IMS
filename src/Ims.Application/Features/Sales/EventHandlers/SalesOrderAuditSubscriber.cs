using Ims.Domain;
namespace Ims.Application.Features.Sales;

/// <summary>
/// Writes the audit row of every change to a sales order (DDD-11b, docs/architecture/ddd-roadmap.md 2.4, 2.5). It runs INSIDE the transaction of the use case, before the unit of work saves, so the row is written by the
/// same save as the change and commits or rolls back with it (AUTH-03). It reads ONLY the facts of the event (never an entity, never a <c>REC_*</c> column) and stages the row through <see cref="IAuditTrail"/> as the
/// handlers did before: the same action, entity type, entity id (the order number), the same Before and After keys in the same order, the same <see cref="AuditFields"/> tag on the selling amount (so the scrubber
/// still withholds it from a caller without <c>so.manage</c>), and the actor and the time are read from the current user and the clock at this moment, as <c>AuditTrail.Record</c> does.
/// <para>
/// Not here: the price-change row (<c>SalesOrderPriceAudit</c>, which needs the price list and runs before the save, so Create and Update dispatch the events first to keep Created before PriceChanged), and the
/// row for an order that posting closes as fully delivered, which <c>SalesDeliveryPosting</c> still writes itself (it moves with DDD-17). The event of that close is skipped below, or the row would be written twice.
/// </para>
/// </summary>
public sealed class SalesOrderAuditSubscriber(IAuditTrail audit) : IAppService,
    IDomainEventHandler<SalesOrderCreated>, IDomainEventHandler<SalesOrderUpdated>, IDomainEventHandler<SalesOrderSubmitted>, IDomainEventHandler<SalesOrderApproved>,
    IDomainEventHandler<SalesOrderRejected>, IDomainEventHandler<SalesOrderRecalled>, IDomainEventHandler<SalesOrderCancelled>, IDomainEventHandler<SalesOrderClosed>
{
    public Task HandleAsync(SalesOrderCreated domainEvent, CancellationToken ct)
    {
        audit.Record(AuditActions.SalesOrderCreated, AuditEntities.SalesOrder, domainEvent.No, after: Summary(domainEvent.Summary));
        return Task.CompletedTask;
    }

    public Task HandleAsync(SalesOrderUpdated domainEvent, CancellationToken ct)
    {
        audit.Record(AuditActions.SalesOrderUpdated, AuditEntities.SalesOrder, domainEvent.No, Summary(domainEvent.Before), Summary(domainEvent.After));
        return Task.CompletedTask;
    }

    public Task HandleAsync(SalesOrderSubmitted domainEvent, CancellationToken ct)
    {
        audit.Record(AuditActions.SalesOrderSubmitted, AuditEntities.SalesOrder, domainEvent.No, new { Status = domainEvent.StatusBefore },
            new Dictionary<string, object?>
            {
                ["Status"] = domainEvent.StatusAfter,
                ["RequiresApproval"] = domainEvent.RequiresApproval,
                ["IsLarge"] = domainEvent.IsLarge,
                [AuditFields.SellingAmount] = domainEvent.SellingAmount.Value
            });
        return Task.CompletedTask;
    }

    public Task HandleAsync(SalesOrderApproved domainEvent, CancellationToken ct)
    {
        audit.Record(AuditActions.SalesOrderApproved, AuditEntities.SalesOrder, domainEvent.No, new { Status = domainEvent.StatusBefore }, new { Status = domainEvent.StatusAfter, domainEvent.IsLarge });
        return Task.CompletedTask;
    }

    public Task HandleAsync(SalesOrderRejected domainEvent, CancellationToken ct)
    {
        audit.Record(AuditActions.SalesOrderRejected, AuditEntities.SalesOrder, domainEvent.No, new { Status = domainEvent.StatusBefore }, new { Status = domainEvent.StatusAfter, domainEvent.Reason });
        return Task.CompletedTask;
    }

    public Task HandleAsync(SalesOrderRecalled domainEvent, CancellationToken ct)
    {
        audit.Record(AuditActions.SalesOrderRecalled, AuditEntities.SalesOrder, domainEvent.No, new { Status = domainEvent.StatusBefore, RequiresApproval = domainEvent.RequiresApprovalBefore }, new { Status = domainEvent.StatusAfter });
        return Task.CompletedTask;
    }

    public Task HandleAsync(SalesOrderCancelled domainEvent, CancellationToken ct)
    {
        audit.Record(AuditActions.SalesOrderCancelled, AuditEntities.SalesOrder, domainEvent.No, new { Status = domainEvent.StatusBefore }, new { Status = domainEvent.StatusAfter, domainEvent.Reason });
        return Task.CompletedTask;
    }

    public Task HandleAsync(SalesOrderClosed domainEvent, CancellationToken ct)
    {
        // Posting the last delivery closes the order as Delivered and SalesDeliveryPosting writes that row itself, in its place in the order of the rows (before the delivery's own Posted row). Until DDD-17
        // moves it here, handling that event as well would write the row twice.
        if (domainEvent.Kind == SalesOrderCloseKind.Delivered) return Task.CompletedTask;

        audit.Record(AuditActions.SalesOrderClosed, AuditEntities.SalesOrder, domainEvent.No, new { Status = domainEvent.StatusBefore }, new { Status = domainEvent.StatusAfter, CloseKind = domainEvent.Kind, domainEvent.Reason });
        return Task.CompletedTask;
    }

    /// <summary>The payload of a create and of both sides of an update, keys in the order the handlers always wrote them.</summary>
    private static Dictionary<string, object?> Summary(SalesOrderSummary summary) => new()
    {
        ["Status"] = summary.Status,
        ["WarehouseId"] = summary.WarehouseId,
        ["CustomerId"] = summary.CustomerId,
        ["Lines"] = summary.LineCount,
        [AuditFields.SellingAmount] = summary.SellingAmount
    };
}
