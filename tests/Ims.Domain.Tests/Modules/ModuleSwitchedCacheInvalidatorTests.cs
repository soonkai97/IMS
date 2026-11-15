using Ims.Application;
using Ims.Application.Features.Modules;

namespace Ims.Domain.Tests.Modules;

/// <summary>
/// DDD-14: dropping the cached switches after a flip is an effect that must never roll the flip back, so it is an after-commit handler of <see cref="ModuleSwitched"/> (docs/architecture/ddd-roadmap.md 2.4, 2.5). It does
/// that and nothing else: it holds no unit of work and no repository, so it cannot save (the dispatcher refuses a save from a handler anyway). <c>ModuleSwitchedAfterCommitTests</c> (Api) proves when it runs against the real unit of work.
/// </summary>
public class ModuleSwitchedCacheInvalidatorTests
{
    private sealed class Switches : IModuleSwitches
    {
        public int Invalidations { get; private set; }

        public Task<bool> IsEnabledAsync(string code, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<string>> EnabledAsync(CancellationToken ct) => throw new NotSupportedException();
        public void Invalidate() => Invalidations++;
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TheCachedSwitchesAreDroppedOnce_ForAFlipEitherWay(bool enabled)
    {
        var switches = new Switches();
        IAfterCommitHandler<ModuleSwitched> handler = new ModuleSwitchedCacheInvalidator(switches);

        await handler.HandleAsync(new ModuleSwitched(ModuleCodes.Sales, EnabledBefore: !enabled, EnabledAfter: enabled, "Good reason", RolesRegainingGrants: 0), CancellationToken.None);

        Assert.Equal(1, switches.Invalidations);
    }

    [Fact]
    public void TheHandlerTakesOnlyTheSwitches_SoItCannotSaveOrLock()
    {
        var parameters = typeof(ModuleSwitchedCacheInvalidator).GetConstructors().Single().GetParameters().Select(p => p.ParameterType);

        Assert.Equal([typeof(IModuleSwitches)], parameters);
    }
}
