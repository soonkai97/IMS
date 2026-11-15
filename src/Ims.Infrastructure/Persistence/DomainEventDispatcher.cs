using Ims.Application;

namespace Ims.Infrastructure.Persistence;

/// <summary>The dispatcher of domain events (docs/architecture/ddd-roadmap.md DDD-11a).</summary>
public sealed class DomainEventDispatcher : IDomainEventDispatcher
{
    public Task DispatchAsync(CancellationToken ct = default) => Task.CompletedTask;
}
