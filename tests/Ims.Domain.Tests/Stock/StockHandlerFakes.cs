using Ardalis.Specification;
using Ims.Application;
using Ims.Application.Features.Documents;
using Ims.Application.Features.Purchasing;
using Ims.Application.Features.Returns;
using Ims.Application.Features.Sales;

namespace Ims.Domain.Tests.Stock;

/// <summary>
/// Hand-written stand-ins for the services the document handlers call, so a handler runs without a database and writes down, in one journal, what it asked for and in what order
/// (the locks, the queries, the audit row, the save). Every member a test does not need throws, so a handler that starts asking for more fails loudly.
/// </summary>
internal sealed class Journal : List<string>;

/// <summary>Holds the rows and answers reads by running the handler's own specification over them, so the filter under test is the real one.</summary>
internal sealed class FakeRepository<T>(IEnumerable<T> rows, Journal journal, string? readNote = null) : IRepository<T> where T : class
{
    private readonly List<T> _rows = rows.ToList();

    public Task<T?> FirstOrDefaultAsync(ISpecification<T> specification, CancellationToken cancellationToken = default) => Task.FromResult(specification.Evaluate(_rows).FirstOrDefault());

    public Task<bool> AnyAsync(ISpecification<T> specification, CancellationToken cancellationToken = default)
    {
        journal.Add(readNote ?? "query");
        return Task.FromResult(specification.Evaluate(_rows).Any());
    }

    public Task<List<T>> ListAsync(ISpecification<T> specification, CancellationToken cancellationToken = default)
    {
        journal.Add(readNote ?? "query");
        return Task.FromResult(specification.Evaluate(_rows).ToList());
    }

    public void Add(T entity) => throw new NotSupportedException();
    public void AddRange(IEnumerable<T> entities) => throw new NotSupportedException();
    public void Remove(T entity) => throw new NotSupportedException();
    public void RemoveRange(IEnumerable<T> entities) => throw new NotSupportedException();
    public Task<List<T>> ListAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<List<TResult>> ListAsync<TResult>(ISpecification<T, TResult> specification, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<T?> GetByIdAsync<TId>(TId id, CancellationToken cancellationToken = default) where TId : notnull => throw new NotSupportedException();
    public Task<TResult?> FirstOrDefaultAsync<TResult>(ISpecification<T, TResult> specification, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<T?> SingleOrDefaultAsync(ISingleResultSpecification<T> specification, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<TResult?> SingleOrDefaultAsync<TResult>(ISingleResultSpecification<T, TResult> specification, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<int> CountAsync(ISpecification<T> specification, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<int> CountAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<bool> AnyAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public IAsyncEnumerable<T> AsAsyncEnumerable(ISpecification<T> specification) => throw new NotSupportedException();
}

/// <summary>Stops a handler at the first call after the step under test, so the rest of it (posting, ledger) need not be faked.</summary>
internal sealed class StopHere : Exception;

internal sealed class RecordingLocker(Journal journal) : ITransactionLocker
{
    public Task LockSalesOrderAsync(int orderId, CancellationToken ct) => Note($"lock so {orderId}");
    public Task LockReturnOriginAsync(int documentId, CancellationToken ct) => Note($"lock ret {documentId}");
    public Task LockStockAsync(int warehouseId, IReadOnlyCollection<int> productIds, CancellationToken ct) => throw new NotSupportedException();
    public Task LockDocumentSequenceAsync(string prefix, int yearMonth, int warehouseId, CancellationToken ct) => throw new NotSupportedException();
    public Task LockRolesAsync(CancellationToken ct) => throw new NotSupportedException();
    public Task LockModulesAsync(CancellationToken ct) => throw new NotSupportedException();
    public Task LockModulesSharedAsync(CancellationToken ct) => throw new NotSupportedException();
    public Task LockPurchaseOrderAsync(int orderId, CancellationToken ct) => throw new NotSupportedException();

    private Task Note(string step)
    {
        journal.Add(step);
        return Task.CompletedTask;
    }
}

internal sealed class RecordingWork(Journal journal) : IUnitOfWork
{
    public Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        journal.Add("save");
        return Task.FromResult(1);
    }

    public Task<T> ExecuteAsync<T>(Func<Task<T>> work, CancellationToken ct = default) => work();
}

internal sealed class RecordingAudit(Journal journal) : IAuditTrail
{
    public void Record(string action, string entityType, string entityId, object? before = null, object? after = null, string? actor = null) => journal.Add($"audit {action}");
}

/// <summary>The settings; <paramref name="stopAfterReading"/> makes the read the place a handler is stopped.</summary>
internal sealed class FakeSettings(Journal journal, bool allowNegative = false, bool allowSelfApproval = false, bool stopAfterReading = false) : ISystemSettings
{
    public Task<SystemSettings> GetAsync(CancellationToken ct)
    {
        journal.Add("settings");
        return stopAfterReading ? throw new StopHere() : Task.FromResult(TestData.Settings(allowNegative, allowSelfApproval));
    }

    public void Invalidate() { }
}

internal sealed class FakeUser(int userId) : ICurrentUser
{
    public int UserId => userId;
    public bool IsAuthenticated => true;
    public string Username => throw new NotSupportedException();
    public string Role => throw new NotSupportedException();
    public IReadOnlyCollection<int> WarehouseIds => throw new NotSupportedException();
    public bool CanAccessWarehouse(int warehouseId) => throw new NotSupportedException();
    public bool AllWarehouses => true;
    public ActorRights Rights() => throw new NotSupportedException();
}

internal sealed class FakeClock : IClock
{
    public DateTime UtcNow => TestData.Now;
}

/// <summary>The caller may do everything and the version is always current: the handlers' own steps are what the tests look at.</summary>
internal sealed class OpenAccess : IDocumentAccess, IDocumentVersion
{
    public void EnsureDocumentInScope(int warehouseId) { }
    public void EnsureMayEditOrSubmit(StockDocument document) { }
    public void EnsureCurrent(StockDocument document, string? expectedToken) { }
    public void EnsureWarehouse(int warehouseId) => throw new NotSupportedException();
    public IReadOnlyCollection<int>? WarehouseScope() => throw new NotSupportedException();
    public void EnsureMayCancel(StockDocument document) => throw new NotSupportedException();
    public void EnsureMayRecall(StockDocument document) => throw new NotSupportedException();
    public bool IsRepeatedRecall(StockDocument document) => throw new NotSupportedException();
    public IReadOnlyList<string> AllowedActions(DocumentDto document, SystemSettings settings, bool withinApprovalLimit, LinkedReceiptContext? link = null) => throw new NotSupportedException();
    public bool CanAct(DocumentListItem item, SystemSettings settings) => throw new NotSupportedException();
    public string Token(Guid publicId, byte[]? rowVersion) => throw new NotSupportedException();
    public void EnsureCurrent(Guid publicId, string no, AuditableEntity row, string? expectedToken) => throw new NotSupportedException();
}

internal sealed class FakeReader : IDocumentReader
{
    public Task<DocumentDto> GetAsync(Guid publicId, CancellationToken ct) => Task.FromResult<DocumentDto>(null!);
}

/// <summary>The reviewer is always within their limit; the assessment is the one given.</summary>
internal sealed class FakeApprovals(Journal journal, bool required = false) : IApprovalEvaluator
{
    public Task<ApprovalAssessment> AssessAsync(StockDocument document, CancellationToken ct)
    {
        journal.Add("assess");
        return Task.FromResult(new ApprovalAssessment(required, 0m));
    }

    public Task<string?> ReviewRefusalAsync(DocumentType type, bool isLarge, decimal totalQty, decimal totalAmount, CancellationToken ct, bool isReturn = false) => Task.FromResult<string?>(null);
    public Task<ApprovalAssessment> AssessAsync(DocumentType type, decimal totalQty, decimal totalAmount, CancellationToken ct) => throw new NotSupportedException();
    public Task<IReadOnlyList<ApprovalTrigger>> TriggersAsync(DocumentType type, decimal totalQty, decimal totalAmount, CancellationToken ct, bool isReturn = false) => throw new NotSupportedException();
}

/// <summary>The three link checks of a submit and the valuation: they find nothing to refuse (the tables use documents without links).</summary>
internal sealed class NoLinkChecks(Journal journal) : IReceiptLinkRules, ISalesDeliveryRules, IReturnRules, IIssueValuation
{
    public Task EnsureCanSubmitAsync(StockDocument draft, CancellationToken ct) => Task.CompletedTask;
    public Task EstimateAsync(StockDocument document, CancellationToken ct)
    {
        journal.Add("valuation");
        return Task.CompletedTask;
    }

    Task<PurchaseOrder> IReceiptLinkRules.OrderForSaveAsync(StockDocument draft, Partner? supplier, CancellationToken ct) => throw new NotSupportedException();
    Task IReceiptLinkRules.EnsureWithinRemainingAsync(PurchaseOrder order, IReadOnlyList<StockDocumentLine> lines, CancellationToken ct) => throw new NotSupportedException();
    Task<SalesOrder> ISalesDeliveryRules.OrderForSaveAsync(StockDocument draft, Partner? customer, IssuePurpose? purpose, CancellationToken ct) => throw new NotSupportedException();
    Task ISalesDeliveryRules.EnsureWithinRemainingAsync(SalesOrder order, IReadOnlyList<StockDocumentLine> lines, CancellationToken ct) => throw new NotSupportedException();
    Task<StockDocument> IReturnRules.OriginalForSaveAsync(StockDocument draft, Partner? partner, IssuePurpose? purpose, CancellationToken ct) => throw new NotSupportedException();
    Task IReturnRules.EnsureWithinRemainingAsync(StockDocument original, IReadOnlyList<StockDocumentLine> lines, CancellationToken ct) => throw new NotSupportedException();
}
