namespace Ims.Application;

/// <summary>The single entry point from the Api into the Application layer.</summary>
public interface IDispatcher
{
    Task<TResult> SendAsync<TResult>(IRequest<TResult> request, CancellationToken ct = default);
}
