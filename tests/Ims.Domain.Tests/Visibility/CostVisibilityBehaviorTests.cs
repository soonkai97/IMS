using Ims.Application;
using Ims.Application.Features.Stock;

namespace Ims.Domain.Tests.Visibility;

/// <summary>The pipeline step that applies the cost rule to every request: a restricted sort key is refused first, the result is shaped last, once.</summary>
public class CostVisibilityBehaviorTests
{
    private sealed class Caller(params string[] permissions) : ICallerPermissions
    {
        public bool Has(string permission) => permissions.Contains(permission);
    }

    private record Money([property: VisibleWith(Permissions.CostView)] decimal? Cost);

    private record Ask(StockFilter Filter) : IQuery<Money>;

    private record AskPlain(Money Inner) : IQuery<Money>;

    private static StockFilter Sorted(StockSort sort) => new(null, null, null, null, SortBy: sort);

    [Fact]
    public async Task SortingByValue_IsRefusedWith400_WithoutTheValuationPermission_AndTheHandlerNeverRuns()
    {
        var behavior = new CostVisibilityBehavior(new Caller(Permissions.CostView), new ShapingState());
        var ran = false;

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            behavior.HandleAsync(new Ask(Sorted(StockSort.Value)), () => { ran = true; return Task.FromResult(new Money(5m)); }, CancellationToken.None));

        Assert.False(ran);
        Assert.Equal(Messages.RestrictedSortKey("Value"), ex.Message);
        Assert.Contains("sortBy", ex.Errors.Keys);
    }

    [Fact]
    public async Task OnlyTheOutermostResultIsShaped_SoAHandlerThatSendsACommandReadsTheFullValues()
    {
        var state = new ShapingState();
        var behavior = new CostVisibilityBehavior(new Caller(), state);
        Money? seenByTheHandler = null;

        var outer = await behavior.HandleAsync(new AskPlain(new Money(1m)), async () =>
        {
            seenByTheHandler = await behavior.HandleAsync(new AskPlain(new Money(2m)), () => Task.FromResult(new Money(7m)), CancellationToken.None);
            return new Money(9m);
        }, CancellationToken.None);

        Assert.Equal(7m, seenByTheHandler!.Cost); // the nested result is complete
        Assert.Null(outer.Cost);                  // what leaves the application is not
        Assert.Equal(0, state.Depth);
    }

    [Fact]
    public async Task AFailingHandler_LeavesTheDepthAtZero_SoTheNextRequestIsStillShaped()
    {
        var state = new ShapingState();
        var behavior = new CostVisibilityBehavior(new Caller(), state);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            behavior.HandleAsync(new AskPlain(new Money(1m)), () => throw new InvalidOperationException(), CancellationToken.None));
        var next = await behavior.HandleAsync(new AskPlain(new Money(1m)), () => Task.FromResult(new Money(3m)), CancellationToken.None);

        Assert.Null(next.Cost);
    }
}
