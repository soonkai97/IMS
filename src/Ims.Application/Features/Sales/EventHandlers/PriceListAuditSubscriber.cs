using Ims.Domain;
namespace Ims.Application.Features.Sales;

/// <summary>STUB (DDD-13 red commit): subscribes and writes nothing yet.</summary>
public sealed class PriceListAuditSubscriber(IAuditTrail audit, IPriceListLabelReader labels) : IAppService,
    IDomainEventHandler<PriceListCreated>, IDomainEventHandler<PriceListUpdated>, IDomainEventHandler<PriceListCustomerSet>, IDomainEventHandler<PriceListDeleted>
{
    private readonly IAuditTrail _audit = audit;
    private readonly IPriceListLabelReader _labels = labels;

    public Task HandleAsync(PriceListCreated domainEvent, CancellationToken ct) => Task.CompletedTask;

    public Task HandleAsync(PriceListUpdated domainEvent, CancellationToken ct) => Task.CompletedTask;

    public Task HandleAsync(PriceListCustomerSet domainEvent, CancellationToken ct) => Task.CompletedTask;

    public Task HandleAsync(PriceListDeleted domainEvent, CancellationToken ct) => Task.CompletedTask;
}
