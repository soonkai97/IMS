using Ims.Domain;
namespace Ims.Application.Features.Modules;

/// <summary>
/// Drops the cached module switches once a flip has COMMITTED (DDD-14, docs/architecture/ddd-roadmap.md 2.4, 2.5): the next question in the request reads the rows again. It is an after-commit handler because it must never roll
/// the flip back, and it runs once, only for the attempt that committed (never for an attempt that was retried or that failed). It takes only the switches: an after-commit handler may not use the unit of work or save.
/// </summary>
public sealed class ModuleSwitchedCacheInvalidator(IModuleSwitches switches) : IAppService, IAfterCommitHandler<ModuleSwitched>
{
    public Task HandleAsync(ModuleSwitched domainEvent, CancellationToken ct)
    {
        switches.Invalidate();
        return Task.CompletedTask;
    }
}
