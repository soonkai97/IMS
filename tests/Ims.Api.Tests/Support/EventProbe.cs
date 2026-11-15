using System.Collections.Concurrent;
using Ims.Application;
using Ims.Domain;

namespace Ims.Api.Tests.Support;

/// <summary>
/// Test-only subscribers for the domain-event dispatcher (DDD-11a). Nothing in the application subscribes yet, so a test host registers <see cref="ProbeHandler{TEvent}"/> and
/// <see cref="ProbeAfterCommitHandler{TEvent}"/> as open generics for every event and steers them through the hooks of one <see cref="EventProbe"/>.
/// </summary>
public sealed class EventProbe
{
    /// <summary>Every event an in-transaction handler received, in the order received (also the events of attempts that were rolled back).</summary>
    public ConcurrentQueue<IDomainEvent> InTransaction { get; } = new();

    /// <summary>Every event an after-commit handler received.</summary>
    public ConcurrentQueue<IDomainEvent> AfterCommit { get; } = new();

    /// <summary>Runs inside the in-transaction handler, with the service provider of the scope that is saving; it may stage changes or throw.</summary>
    public Func<IDomainEvent, IServiceProvider, Task>? OnInTransaction { get; set; }

    /// <summary>Runs inside the after-commit handler, after it was counted; it may throw.</summary>
    public Func<IDomainEvent, IServiceProvider, Task>? OnAfterCommit { get; set; }
}

public sealed class ProbeHandler<TEvent>(EventProbe probe, IServiceProvider services) : IDomainEventHandler<TEvent> where TEvent : IDomainEvent
{
    public async Task HandleAsync(TEvent domainEvent, CancellationToken ct)
    {
        probe.InTransaction.Enqueue(domainEvent);
        if (probe.OnInTransaction is { } hook) await hook(domainEvent, services);
    }
}

public sealed class ProbeAfterCommitHandler<TEvent>(EventProbe probe, IServiceProvider services) : IAfterCommitHandler<TEvent> where TEvent : IDomainEvent
{
    public async Task HandleAsync(TEvent domainEvent, CancellationToken ct)
    {
        probe.AfterCommit.Enqueue(domainEvent);
        if (probe.OnAfterCommit is { } hook) await hook(domainEvent, services);
    }
}
