using Ims.Domain;
namespace Ims.Application.Features.Sales;

/// <summary>RED STUB (DDD-11b, commit 1): compiles so that the new tests fail for the right reason; it writes nothing yet.</summary>
public sealed class SalesOrderAuditSubscriber(IAuditTrail audit) : IAppService,
    IDomainEventHandler<SalesOrderCreated>, IDomainEventHandler<SalesOrderUpdated>, IDomainEventHandler<SalesOrderSubmitted>, IDomainEventHandler<SalesOrderApproved>,
    IDomainEventHandler<SalesOrderRejected>, IDomainEventHandler<SalesOrderRecalled>, IDomainEventHandler<SalesOrderCancelled>, IDomainEventHandler<SalesOrderClosed>
{
    private readonly IAuditTrail _audit = audit;

    public Task HandleAsync(SalesOrderCreated domainEvent, CancellationToken ct) => Done();
    public Task HandleAsync(SalesOrderUpdated domainEvent, CancellationToken ct) => Done();
    public Task HandleAsync(SalesOrderSubmitted domainEvent, CancellationToken ct) => Done();
    public Task HandleAsync(SalesOrderApproved domainEvent, CancellationToken ct) => Done();
    public Task HandleAsync(SalesOrderRejected domainEvent, CancellationToken ct) => Done();
    public Task HandleAsync(SalesOrderRecalled domainEvent, CancellationToken ct) => Done();
    public Task HandleAsync(SalesOrderCancelled domainEvent, CancellationToken ct) => Done();
    public Task HandleAsync(SalesOrderClosed domainEvent, CancellationToken ct) => Done();

    private Task Done() => _audit is null ? throw new InvalidOperationException() : Task.CompletedTask;
}
