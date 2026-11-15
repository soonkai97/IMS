using Ims.Application;
using Ims.Domain;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

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

    // ----- a transaction is opened by the unit of work only -----

    private static readonly string[] MayOpenATransaction = ["Ims.Infrastructure/Persistence/UnitOfWork.cs", "Ims.Infrastructure/Persistence/AppDbContext.cs"];

    /// <summary>
    /// The after-commit queue is flushed by <c>UnitOfWork</c> (after <c>ExecuteAsync</c> commits, or after a bare save). A transaction opened anywhere else would commit without flushing it, so no code under src
    /// opens one but the unit of work and the context's own audit save.
    /// </summary>
    [Fact]
    public void NoTransactionIsOpenedOutsideTheUnitOfWork()
    {
        var opening = SourceFiles.All.Where(f => OpensATransaction(f.Root)).ToList();
        Assert.Contains(opening, f => f.RelativePath == MayOpenATransaction[0]);   // the rule is not vacuous

        Verify.NoOffenders(opening.Select(f => f.RelativePath).Except(MayOpenATransaction), "BeginTransaction may be called only in UnitOfWork and AppDbContext (an after-commit queue is flushed by the unit of work):");
    }

    [Theory]
    [InlineData("class C { async Task M(DbContext db) { await using var tx = await db.Database.BeginTransactionAsync(ct); } }", true)]
    [InlineData("class C { void M(DbContext db) { db.Database.BeginTransaction(); } }", true)]
    [InlineData("class C { void M(SqlConnection c) { c.BeginTransaction(); } }", true)]
    [InlineData("class C { void M() { var name = \"BeginTransaction\"; /* BeginTransaction() */ } }", false)]
    [InlineData("class C { void M(IUnitOfWork uow) { uow.ExecuteAsync(() => null); } }", false)]
    public void TheTransactionRule_FindsACallThatOpensOne(string code, bool found) => Assert.Equal(found, OpensATransaction(CSharpSyntaxTree.ParseText(code).GetRoot()));

    private static bool OpensATransaction(SyntaxNode root) => root.DescendantNodes().OfType<InvocationExpressionSyntax>()
        .Any(i => (i.Expression is MemberAccessExpressionSyntax member ? member.Name.Identifier.Text : (i.Expression as IdentifierNameSyntax)?.Identifier.Text) is "BeginTransaction" or "BeginTransactionAsync");
}
