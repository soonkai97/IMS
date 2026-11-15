using System.Collections.Concurrent;
using Ims.Application;
using Ims.Domain;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Ims.Infrastructure.Persistence;

/// <summary>
/// Hands the domain events of the tracked aggregates to their handlers (docs/architecture/ddd-roadmap.md 2.5, DDD-11a). One instance per request scope, shared by the use case and its unit of work.
/// <list type="bullet">
/// <item><see cref="DispatchAsync"/> runs in the transaction, at the top of every save: it takes the events (and clears them from the aggregates at once, so an event is delivered once), sorts them by the
/// sequence the domain gave them, and runs the in-transaction handlers in rounds. Events that handlers raise are found by the next scan and go in the next round; after <see cref="MaxRounds"/> it throws.</item>
/// <item>The after-commit handlers of every delivered event are queued, not run. <see cref="UnitOfWork"/> runs them with <see cref="FlushAfterCommitAsync"/> once the outermost transaction has committed, or
/// drops them with <see cref="Discard"/> when the attempt or the use case failed. A failing after-commit handler is logged and swallowed: the request has already succeeded.</item>
/// <item>While a handler runs (<see cref="IsRunning"/>) the unit of work refuses a save: a handler stages changes, the save that is dispatching writes them.</item>
/// </list>
/// The scan reads the change tracker in memory (<c>Entries</c>), so it sends no SQL.
/// </summary>
public sealed class DomainEventDispatcher(AppDbContext db, IServiceProvider services, ILogger<DomainEventDispatcher> log) : IDomainEventDispatcher
{
    /// <summary>Rounds a dispatch may run: a handler that raises an event for each event it handles never ends, so the transaction fails instead of spinning.</summary>
    public const int MaxRounds = 100;

    internal const string HandlerSaved = "An event handler must not save: the dispatcher runs inside the save, and the handler's staged changes are written by it.";
    internal const string HandlerDispatched = "An event handler must not dispatch events: the dispatcher is already running.";
    internal const string TooManyRounds = "Domain event handlers raised events for more than 100 rounds in one save; a handler raises an event for each event it handles.";

    private static readonly ConcurrentDictionary<Type, EventInvoker> Invokers = new();

    private readonly List<PendingAfterCommit> _afterCommit = [];

    /// <summary>True while an in-transaction or an after-commit handler runs.</summary>
    internal bool IsRunning { get; private set; }

    public async Task DispatchAsync(CancellationToken ct = default)
    {
        if (IsRunning) throw new InvalidOperationException(HandlerDispatched);

        IsRunning = true;
        try
        {
            for (var round = 0; ; round++)
            {
                var raised = Take();
                if (raised.Count == 0) return;
                if (round >= MaxRounds) throw new InvalidOperationException(TooManyRounds);

                foreach (var (_, domainEvent) in raised)
                {
                    var invoker = Invokers.GetOrAdd(domainEvent.GetType(), CreateInvoker);
                    await invoker.HandleAsync(services, domainEvent, ct);
                    invoker.QueueAfterCommit(services, domainEvent, _afterCommit);
                }
            }
        }
        finally
        {
            IsRunning = false;
        }
    }

    /// <summary>Drops the queued after-commit handlers: the attempt was rolled back or the use case failed, so nothing it raised happened.</summary>
    internal void Discard() => _afterCommit.Clear();

    /// <summary>
    /// Runs the queued after-commit handlers once, in the order the events were raised, with no cancellation (the work is committed). It never throws: a failure is logged and the next handler still runs.
    /// </summary>
    internal async Task FlushAfterCommitAsync()
    {
        if (_afterCommit.Count == 0) return;

        var pending = _afterCommit.ToList();
        _afterCommit.Clear();
        IsRunning = true;
        try
        {
            foreach (var item in pending)
            {
                try
                {
                    await item.Run(CancellationToken.None);
                }
                catch (Exception ex)
                {
                    log.LogError(ex, "After-commit handler {Handler} failed for {Event}; the committed request is not affected", item.Handler, item.Event);
                }
            }
        }
        finally
        {
            IsRunning = false;
        }
    }

    /// <summary>
    /// Takes the events of every tracked aggregate (any state but Detached), clears them from the aggregates and returns them oldest first. The scan reads the tracker, which first detects changes, so a
    /// root that was changed through a navigation is found as well; the save that follows detects changes again.
    /// </summary>
    private List<RaisedEvent> Take()
    {
        var raised = new List<RaisedEvent>();
        foreach (var entry in db.ChangeTracker.Entries<IAggregateRoot>())
        {
            if (entry.Entity.RaisedEvents.Count == 0) continue;
            raised.AddRange(entry.Entity.RaisedEvents);
            entry.Entity.ClearDomainEvents();
        }

        raised.Sort((a, b) => a.Sequence.CompareTo(b.Sequence));
        return raised;
    }

    private static EventInvoker CreateInvoker(Type eventType) => (EventInvoker)Activator.CreateInstance(typeof(EventInvoker<>).MakeGenericType(eventType))!;

    private sealed record PendingAfterCommit(string Handler, string Event, Func<CancellationToken, Task> Run);

    private abstract class EventInvoker
    {
        public abstract Task HandleAsync(IServiceProvider services, IDomainEvent domainEvent, CancellationToken ct);

        public abstract void QueueAfterCommit(IServiceProvider services, IDomainEvent domainEvent, List<PendingAfterCommit> queue);
    }

    private sealed class EventInvoker<TEvent> : EventInvoker where TEvent : IDomainEvent
    {
        public override async Task HandleAsync(IServiceProvider services, IDomainEvent domainEvent, CancellationToken ct)
        {
            foreach (var handler in services.GetServices<IDomainEventHandler<TEvent>>())
                await handler.HandleAsync((TEvent)domainEvent, ct);
        }

        public override void QueueAfterCommit(IServiceProvider services, IDomainEvent domainEvent, List<PendingAfterCommit> queue)
        {
            foreach (var handler in services.GetServices<IAfterCommitHandler<TEvent>>())
                queue.Add(new PendingAfterCommit(handler.GetType().Name, typeof(TEvent).Name, ct => handler.HandleAsync((TEvent)domainEvent, ct)));
        }
    }
}
