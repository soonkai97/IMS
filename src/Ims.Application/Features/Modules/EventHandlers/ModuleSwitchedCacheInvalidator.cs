using Ims.Domain;
namespace Ims.Application.Features.Modules;

public sealed class ModuleSwitchedCacheInvalidator(IModuleSwitches switches) : IAppService, IAfterCommitHandler<ModuleSwitched>
{
    public Task HandleAsync(ModuleSwitched domainEvent, CancellationToken ct) { _ = switches; return Task.CompletedTask; }
}
