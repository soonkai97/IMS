using Ims.Application;
using Ims.Domain;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Ims.Architecture.Tests;

/// <summary>
/// DDD-11a, docs/architecture/ddd-roadmap.md 2.5: an event handler runs inside the locked transaction of a use case (or after it), so it never takes a lock of its own: the use case already holds
/// its locks in the global order, and a lock taken from a handler could break that order. SalesOrderAuditSubscriber (DDD-11b) is the first handler; the rule is also proved on synthetic handlers so it is not vacuous.
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

    private sealed class SavingAfterCommit(IUnitOfWork uow) : IAfterCommitHandler<SomethingHappened>
    {
        public Task HandleAsync(SomethingHappened domainEvent, CancellationToken ct) => uow.SaveChangesAsync(ct);
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

    // ----- an after-commit handler holds no unit of work -----

    private static bool IsAfterCommitHandler(Type type) =>
        type is { IsClass: true, IsAbstract: false } && type.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IAfterCommitHandler<>));

    private static bool TakesTheUnitOfWork(Type type) => type
        .GetConstructors(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)
        .SelectMany(c => c.GetParameters())
        .Any(p => typeof(IUnitOfWork).IsAssignableFrom(p.ParameterType));

    /// <summary>
    /// An after-commit handler runs once the transaction has committed, for an effect that must never roll the business change back (DDD-14: dropping the cached module switches). What it would write is never
    /// committed: the dispatcher refuses a save and a transaction from a handler (the flush logs the refusal and goes on), so the work would be silently lost. An effect that must be written belongs in an in-transaction handler.
    /// </summary>
    [Fact]
    public void NoAfterCommitHandler_TakesTheUnitOfWork()
    {
        var found = Layers.TypesOf(Layers.Application).Concat(Layers.TypesOf(Layers.Infrastructure)).Where(IsAfterCommitHandler).ToList();
        Assert.Contains(found, t => t.Name == "ModuleSwitchedCacheInvalidator");   // the rule is not vacuous

        Verify.NoOffenders(found.Where(TakesTheUnitOfWork).Select(t => t.FullName!), "An after-commit handler takes IUnitOfWork (what it saved would never be written; stage it in an in-transaction handler):");
    }

    [Theory]
    [InlineData(typeof(CleanInTransaction), false, false)]
    [InlineData(typeof(LockingAfterCommit), true, false)]
    [InlineData(typeof(SavingAfterCommit), true, true)]
    [InlineData(typeof(NotAHandler), false, false)]
    public void TheUnitOfWorkRule_RecognisesAnAfterCommitHandlerAndAUnitOfWorkInItsConstructor(Type type, bool isAfterCommit, bool takesUnitOfWork)
    {
        Assert.Equal(isAfterCommit, IsAfterCommitHandler(type));
        Assert.Equal(takesUnitOfWork, TakesTheUnitOfWork(type));
    }

    // ----- a transaction is opened by the unit of work only -----

    private static readonly string[] MayOpenATransaction = ["Ims.Infrastructure/Persistence/UnitOfWork.cs", "Ims.Infrastructure/Persistence/AppDbContext.cs"];

    /// <summary>
    /// The after-commit queue is flushed by <c>UnitOfWork</c> (after <c>ExecuteAsync</c> commits, or after a bare save). A transaction opened anywhere else would commit without flushing it, so no code under src
    /// opens one (<c>BeginTransaction</c>, <c>UseTransaction</c>, <c>TransactionScope</c>) but the unit of work and the context's own audit save.
    /// </summary>
    [Fact]
    public void NoTransactionIsOpenedOutsideTheUnitOfWork()
    {
        var opening = SourceFiles.All.Where(f => OpensATransaction(f.Root)).ToList();
        Assert.Contains(opening, f => f.RelativePath == MayOpenATransaction[0]);   // the rule is not vacuous

        Verify.NoOffenders(opening.Select(f => f.RelativePath).Except(MayOpenATransaction), "A transaction (BeginTransaction, UseTransaction, TransactionScope) may be opened only in UnitOfWork and AppDbContext (an after-commit queue is flushed by the unit of work):");
    }

    [Theory]
    [InlineData("class C { async Task M(DbContext db) { await using var tx = await db.Database.BeginTransactionAsync(ct); } }", true)]
    [InlineData("class C { void M(DbContext db) { db.Database.BeginTransaction(); } }", true)]
    [InlineData("class C { void M(SqlConnection c) { c.BeginTransaction(); } }", true)]
    [InlineData("class C { void M() { var name = \"BeginTransaction\"; /* BeginTransaction() */ } }", false)]
    [InlineData("class C { void M(IUnitOfWork uow) { uow.ExecuteAsync(() => null); } }", false)]
    [InlineData("class C { void M() { using var scope = new TransactionScope(); scope.Complete(); } }", true)]
    [InlineData("class C { void M() { using var scope = new System.Transactions.TransactionScope(TransactionScopeOption.Required); } }", true)]
    [InlineData("class C { void M() { using var scope = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled); } }", true)]
    [InlineData("class C { void M(DbContext db, DbTransaction tx) { db.Database.UseTransaction(tx); } }", true)]
    [InlineData("class C { async Task M(DbContext db, DbTransaction tx) { await db.Database.UseTransactionAsync(tx, ct); } }", true)]
    [InlineData("class C { void M() { var name = \"TransactionScope\"; var other = \"UseTransaction\"; /* new TransactionScope() */ } }", false)]
    [InlineData("class C { void M() { var t = new TransactionScopeOptions(); } }", false)]
    public void TheTransactionRule_FindsACallThatOpensOne(string code, bool found) => Assert.Equal(found, OpensATransaction(CSharpSyntaxTree.ParseText(code).GetRoot()));

    /// <summary>
    /// A call that opens a transaction or joins one that something else opened: <c>BeginTransaction</c>, <c>BeginTransactionAsync</c>, <c>UseTransaction</c>, <c>UseTransactionAsync</c> and <c>new TransactionScope(...)</c>
    /// (an ambient transaction commits without the unit of work flushing the after-commit queue just the same).
    /// </summary>
    private static bool OpensATransaction(SyntaxNode root) =>
        root.DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Any(i => (i.Expression is MemberAccessExpressionSyntax member ? member.Name.Identifier.Text : (i.Expression as IdentifierNameSyntax)?.Identifier.Text) is "BeginTransaction" or "BeginTransactionAsync" or "UseTransaction" or "UseTransactionAsync")
        || root.DescendantNodes().OfType<ObjectCreationExpressionSyntax>()
            .Any(c => (c.Type is QualifiedNameSyntax qualified ? qualified.Right.Identifier.Text : (c.Type as IdentifierNameSyntax)?.Identifier.Text) == "TransactionScope");
}
