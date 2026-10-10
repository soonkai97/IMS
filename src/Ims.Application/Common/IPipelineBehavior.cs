namespace Ims.Application;

/// <summary>Cross-cutting step around every handler (logging, validation, ...). Registered in order; the first wraps all others.</summary>
public interface IPipelineBehavior
{
    Task<TResult> HandleAsync<TResult>(IRequest<TResult> request, Func<Task<TResult>> next, CancellationToken ct);
}
