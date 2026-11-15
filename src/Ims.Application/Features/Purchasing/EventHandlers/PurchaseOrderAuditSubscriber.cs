using Ims.Domain;
namespace Ims.Application.Features.Purchasing;

/// <summary>Test-first stub (DDD-12): subscribes to the eight events and writes nothing yet.</summary>
public sealed class PurchaseOrderAuditSubscriber(IAuditTrail audit) : IAppService,
    IDomainEventHandler<PurchaseOrderCreated>, IDomainEventHandler<PurchaseOrderUpdated>, IDomainEventHandler<PurchaseOrderSubmitted>, IDomainEventHandler<PurchaseOrderApproved>,
    IDomainEventHandler<PurchaseOrderRejected>, IDomainEventHandler<PurchaseOrderRecalled>, IDomainEventHandler<PurchaseOrderCancelled>, IDomainEventHandler<PurchaseOrderClosed>
{
    private readonly IAuditTrail _audit = audit;

    public Task HandleAsync(PurchaseOrderCreated domainEvent, CancellationToken ct) => Task.CompletedTask;
    public Task HandleAsync(PurchaseOrderUpdated domainEvent, CancellationToken ct) => Task.CompletedTask;
    public Task HandleAsync(PurchaseOrderSubmitted domainEvent, CancellationToken ct) => Task.CompletedTask;
    public Task HandleAsync(PurchaseOrderApproved domainEvent, CancellationToken ct) => Task.CompletedTask;
    public Task HandleAsync(PurchaseOrderRejected domainEvent, CancellationToken ct) => Task.CompletedTask;
    public Task HandleAsync(PurchaseOrderRecalled domainEvent, CancellationToken ct) => Task.CompletedTask;
    public Task HandleAsync(PurchaseOrderCancelled domainEvent, CancellationToken ct) => Task.CompletedTask;
    public Task HandleAsync(PurchaseOrderClosed domainEvent, CancellationToken ct) => Task.CompletedTask;
}
