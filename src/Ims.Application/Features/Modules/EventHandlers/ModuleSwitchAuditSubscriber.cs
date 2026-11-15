using Ims.Domain;
namespace Ims.Application.Features.Modules;

public sealed class ModuleSwitchAuditSubscriber(IAuditTrail audit) : IAppService, IDomainEventHandler<ModuleSwitched>
{
    public Task HandleAsync(ModuleSwitched domainEvent, CancellationToken ct) { _ = audit; return Task.CompletedTask; }
}
