using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;

namespace Ims.Application;

public sealed class Dispatcher(IServiceProvider services) : IDispatcher
{
    private static readonly ConcurrentDictionary<Type, object> Wrappers = new();

    public Task<TResult> SendAsync<TResult>(IRequest<TResult> request, CancellationToken ct = default)
    {
        var wrapper = (HandlerWrapper<TResult>)Wrappers.GetOrAdd(request.GetType(), t =>
            Activator.CreateInstance(typeof(HandlerWrapper<,>).MakeGenericType(t, typeof(TResult)))!);

        Func<Task<TResult>> pipeline = () => wrapper.HandleAsync(services, request, ct);
        foreach (var behavior in services.GetServices<IPipelineBehavior>().Reverse())
        {
            var next = pipeline;
            var b = behavior;
            pipeline = () => b.HandleAsync(request, next, ct);
        }
        return pipeline();
    }

    /// <summary>Typed bridge to the handler: no reflection on the request path, and exceptions are not wrapped.</summary>
    private abstract class HandlerWrapper<TResult>
    {
        public abstract Task<TResult> HandleAsync(IServiceProvider sp, IRequest<TResult> request, CancellationToken ct);
    }

    private sealed class HandlerWrapper<TRequest, TResult> : HandlerWrapper<TResult> where TRequest : IRequest<TResult>
    {
        public override Task<TResult> HandleAsync(IServiceProvider sp, IRequest<TResult> request, CancellationToken ct) =>
            (sp.GetService<IRequestHandler<TRequest, TResult>>()
             ?? throw new InvalidOperationException($"No handler is registered for {typeof(TRequest).Name}."))
            .HandleAsync((TRequest)request, ct);
    }
}
