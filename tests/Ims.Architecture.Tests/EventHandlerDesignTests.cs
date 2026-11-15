using Ims.Application;
using Ims.Domain;

namespace Ims.Architecture.Tests;

/// <summary>
/// DDD-11a, docs/architecture/ddd-roadmap.md 2.5: an event handler runs inside the locked transaction of a use case (or after it), so it never takes a lock of its own: the use case already holds
/// its locks in the global order, and a lock taken from a handler could break that order. No handler exists yet; the rule is proved on synthetic handlers so it is not vacuous.
/// </summary>
public class EventHandlerDesignTests
{
    private sealed record SomethingHappened : IDomainEvent;

    private sealed class CleanInTransaction(IAuditTrail audit) : IDomainEventHandler<SomethingHappened>
    {
        public Task HandleAsync(SomethingHappened domainEvent, CancellationToken ct) { _ = audit; return Task.CompletedTask; }
    }

    private sealed class LockingInTransaction(ITransactionLocker locker) : IDomainEventHandler<SomethingHappened>
    {
        public Task HandleAsync(SomethingHappened domainEvent, CancellationToken ct) { _ = locker; return Task.CompletedTask; }
    }

    private sealed class LockingAfterCommit(IAuditTrail audit, ITransactionLocker locker) : IAfterCommitHandler<SomethingHappened>
    {
        public Task HandleAsync(SomethingHappened domainEvent, CancellationToken ct) { _ = audit; _ = locker; return Task.CompletedTask; }
    }

    private sealed class NotAHandler(ITransactionLocker locker)
    {
        public ITransactionLocker Locker { get; } = locker;
    }

    private static bool IsEventHandler(Type type) =>
        type is { IsClass: true, IsAbstract: false } && type.GetInterfaces().Any(i => i.IsGenericType
            && (i.GetGenericTypeDefinition() == typeof(IDomainEventHandler<>) || i.GetGenericTypeDefinition() == typeof(IAfterCommitHandler<>)));

    private static bool TakesTheLocker(Type type) => type
        .GetConstructors(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)
        .SelectMany(c => c.GetParameters())
        .Any(p => typeof(ITransactionLocker).IsAssignableFrom(p.ParameterType));

    [Fact]
    public void NoEventHandler_TakesTheTransactionLocker()
    {
        var offenders = Layers.TypesOf(Layers.Application).Concat(Layers.TypesOf(Layers.Infrastructure))
            .Where(t => IsEventHandler(t) && TakesTheLocker(t))
            .Select(t => t.FullName!);

        Verify.NoOffenders(offenders, "An event handler takes ITransactionLocker (it runs inside the locks of its use case; take no lock from a handler):");
    }

    [Theory]
    [InlineData(typeof(CleanInTransaction), true, false)]
    [InlineData(typeof(LockingInTransaction), true, true)]
    [InlineData(typeof(LockingAfterCommit), true, true)]
    [InlineData(typeof(NotAHandler), false, true)]
    public void TheRule_RecognisesAHandlerAndALockInItsConstructor(Type type, bool isHandler, bool takesLocker)
    {
        Assert.Equal(isHandler, IsEventHandler(type));
        Assert.Equal(takesLocker, TakesTheLocker(type));
    }
}
