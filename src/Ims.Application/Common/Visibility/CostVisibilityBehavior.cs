namespace Ims.Application;

/// <summary>
/// Applies the cost rule (<see cref="ResponseShaper"/>, <see cref="RequestGuard"/>) to every request that enters through the dispatcher: a restricted
/// sort key is refused before the handler runs, and the response is shaped for the caller after it. Only the outermost request is shaped: a handler
/// that sends another command (the opening-stock import creates receipts) reads the full result, and what it returns is shaped once at the end.
/// </summary>
public sealed class CostVisibilityBehavior(ICallerPermissions caller, ShapingState state) : IPipelineBehavior
{
    public async Task<TResult> HandleAsync<TResult>(IRequest<TResult> request, Func<Task<TResult>> next, CancellationToken ct)
    {
        var outermost = state.Depth == 0;
        if (outermost) RequestGuard.EnsureAllowed(request, caller.Has);

        state.Depth++;
        try
        {
            var result = await next();
            if (outermost) ResponseShaper.Shape(result, caller.Has);
            return result;
        }
        finally
        {
            state.Depth--;
        }
    }
}
