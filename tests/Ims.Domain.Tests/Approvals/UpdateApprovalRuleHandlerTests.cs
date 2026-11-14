using Ardalis.Specification;
using Ims.Application;
using Ims.Application.Features.Modules;
using Ims.Application.Features.Settings;

namespace Ims.Domain.Tests.Approvals;

/// <summary>
/// DDD-10, characterization: <c>UpdateApprovalRuleHandler</c> as it is on <c>main</c> before the two cross-rule invariants move into <c>ApprovalRuleSetPolicy</c>.
/// Green before the change and green after it: the same exception, status class and sentence for every input, and the same order of the lock and the count
/// (the <c>modules</c> lock first, then the count under it, and neither when the answer does not need them). The 400, 404 and 409 mapping is the one of the exception types;
/// <c>Ims.Api.Tests</c> (<c>PurchaseOrderRuleGuardTests</c>, <c>SalesOrderRuleGuardTests</c>) prove the status on the wire.
/// </summary>
public class UpdateApprovalRuleHandlerTests
{
    private const string Lock = "lock modules";
    private const string Count = "count active rules";
    private const string Save = "save";

    [Fact]
    public async Task EveryOldTypeNewTypeActiveStateAndCount_GivesTheSameAnswerThatTheTableWrites()
    {
        var wrong = new List<string>();
        var count = 0;
        foreach (var c in ApprovalRuleChangeTable.All())
        {
            count++;
            var world = new World(c);

            var (outcome, thrown) = await world.RunAsync();

            var expected = ApprovalRuleChangeTable.Expected(c);
            if (outcome != expected) wrong.Add($"{c}: expected {expected}, got {outcome} ({thrown?.GetType().Name})");

            var expectedJournal = new List<string>();
            if (ApprovalRuleChangeTable.NeedsCount(c)) expectedJournal.AddRange([Lock, Count]);
            if (expected == RuleChangeOutcome.None) expectedJournal.Add(Save);
            if (!expectedJournal.SequenceEqual(world.Journal)) wrong.Add($"{c}: expected steps [{string.Join(", ", expectedJournal)}], got [{string.Join(", ", world.Journal)}]");

            if (expected == RuleChangeOutcome.None && (world.Rule.DocType, world.Rule.IsActive) != (c.NewType, c.NewActive)) wrong.Add($"{c}: the rule was not updated");
            if (expected != RuleChangeOutcome.None && (world.Rule.DocType, world.Rule.IsActive) != (c.Type, c.Active)) wrong.Add($"{c}: the refused rule was changed");
        }

        Assert.True(count > 400, $"the table is the whole grid ({count} cases)");
        Assert.Equal(Enum.GetValues<RuleChangeOutcome>().Length, ApprovalRuleChangeTable.All().Select(c => ApprovalRuleChangeTable.Expected(c)).Distinct().Count()); // the grid reaches every outcome
        Assert.True(wrong.Count == 0, $"{wrong.Count} of {count} differ:{Environment.NewLine}{string.Join(Environment.NewLine, wrong.Take(15))}");
    }

    [Fact]
    public async Task TheFourSentencesAreTheOnesTheApiAnswersWith()
    {
        Assert.Equal("An approval rule cannot be moved to or from purchase orders. Add a new rule for the other type instead.", Messages.PurchaseOrderRuleTypeFixed);
        Assert.Equal("An approval rule cannot be moved to or from sales orders. Add a new rule for the other type instead.", Messages.SalesOrderRuleTypeFixed);
        Assert.Equal("This is the last active approval rule for purchase orders. While purchasing is on, at least one must stay active, or every priced order would be released with no approver.", Messages.PurchaseOrderLastRule);
        Assert.Equal("This is the last active approval rule for sales orders. While sales is on, at least one must stay active, or every priced order would be released with no approver.", Messages.SalesOrderLastRule);

        var (_, onto) = await new World(new(DocumentType.Receipt, true, DocumentType.SalesOrder, true, 1)).RunAsync();
        var (_, from) = await new World(new(DocumentType.PurchaseOrder, true, DocumentType.Issue, true, 1)).RunAsync();
        var (_, lastSales) = await new World(new(DocumentType.SalesOrder, true, DocumentType.SalesOrder, false, 1)).RunAsync();
        var (_, lastPurchase) = await new World(new(DocumentType.PurchaseOrder, true, DocumentType.PurchaseOrder, false, 1)).RunAsync();

        Assert.IsType<ValidationException>(onto);
        Assert.Equal(Messages.SalesOrderRuleTypeFixed, onto!.Message);
        Assert.IsType<ValidationException>(from);
        Assert.Equal(Messages.PurchaseOrderRuleTypeFixed, from!.Message);
        Assert.IsType<ConflictException>(lastSales);
        Assert.Equal(Messages.SalesOrderLastRule, lastSales!.Message);
        Assert.IsType<ConflictException>(lastPurchase);
        Assert.Equal(Messages.PurchaseOrderLastRule, lastPurchase!.Message);
    }

    [Fact]
    public async Task AMoveBetweenTheTwoOrderTypes_NamesTheSalesOrder()
    {
        var (_, thrown) = await new World(new(DocumentType.PurchaseOrder, true, DocumentType.SalesOrder, true, 2)).RunAsync();
        var (_, back) = await new World(new(DocumentType.SalesOrder, true, DocumentType.PurchaseOrder, true, 2)).RunAsync();

        Assert.Equal(Messages.SalesOrderRuleTypeFixed, thrown!.Message);
        Assert.Equal(Messages.SalesOrderRuleTypeFixed, back!.Message);
    }

    [Fact]
    public async Task ARuleOfAnOffModule_IsNotFound_BeforeAnythingElseIsAsked()
    {
        // Switching the last rule off while the module is off would be a 409 if the rule were visible; it is not: a hidden rule is a 404 and nothing is locked, counted or saved.
        var world = new World(new(DocumentType.PurchaseOrder, true, DocumentType.PurchaseOrder, false, 1), purchasing: false);

        var (_, thrown) = await world.RunAsync();

        Assert.IsType<NotFoundException>(thrown);
        Assert.Equal(Messages.RuleNotFound, thrown!.Message);
        Assert.Empty(world.Journal);
    }

    [Fact]
    public async Task ANewTypeOfAnOffModule_IsRefusedFirst_ByTheModuleCheck_NotByTheTypeRule()
    {
        // A receipt rule moved to purchase orders while purchasing is off: the module check (400 naming docType) comes before "an order rule keeps its type".
        var world = new World(new(DocumentType.Receipt, true, DocumentType.PurchaseOrder, true, 1), purchasing: false);

        var (_, thrown) = await world.RunAsync();

        Assert.IsType<ValidationException>(thrown);
        Assert.Equal(Messages.ModuleFieldNotAvailable("docType", "purchasing"), thrown!.Message);
        Assert.Empty(world.Journal);
    }

    [Fact]
    public async Task TheLastRuleOfOneOrderType_IsNotAffectedByTheRulesOfTheOther()
    {
        // The count is of the rule's own type: an active sales rule does not keep the last purchase rule alive.
        var world = new World(new(DocumentType.PurchaseOrder, true, DocumentType.PurchaseOrder, false, 1));

        var (_, thrown) = await world.RunAsync();

        Assert.IsType<ConflictException>(thrown);
        Assert.Equal([Lock, Count], world.Journal);
    }

    /// <summary>One rule being edited among the rules of its type and some of other types and states, with the steps the handler takes written down in order.</summary>
    private sealed class World
    {
        private readonly ApprovalRuleChangeTable.Case _case;
        private readonly Switches _modules;
        private readonly Store _rules;
        public List<string> Journal { get; } = [];
        public ApprovalRule Rule { get; }

        public World(ApprovalRuleChangeTable.Case c, bool purchasing = true, bool sales = true)
        {
            _case = c;
            Rule = ApprovalRule.Create(c.Type, 100m, null, ApproverTier.Standard, c.Active);
            var all = new List<ApprovalRule> { Rule };
            for (var i = 0; i < c.ActiveOfType - (c.Active ? 1 : 0); i++) all.Add(ApprovalRule.Create(c.Type, 100m + i, null, ApproverTier.Standard, true));
            // Rules the count must not take: an inactive one of the same type, and an active one of each other type.
            all.Add(ApprovalRule.Create(c.Type, 1m, null, ApproverTier.Standard, false));
            all.AddRange(ApprovalRuleChangeTable.Types.Where(t => t != c.Type).Select(t => ApprovalRule.Create(t, 1m, null, ApproverTier.Standard, true)));
            _rules = new Store(all, Journal);
            _modules = new Switches(purchasing, sales);
        }

        public async Task<(RuleChangeOutcome? Outcome, Exception? Thrown)> RunAsync()
        {
            var handler = new UpdateApprovalRuleHandler(_rules, _modules, new Locker(Journal), new Work(Journal));
            var data = new ApprovalRuleSaveRequest(_case.NewType, 100m, null, _case.NewActive, ApproverTier.Standard);
            try
            {
                await handler.HandleAsync(new UpdateApprovalRuleCommand(Rule.PublicId, data), CancellationToken.None);
                return (RuleChangeOutcome.None, null);
            }
            catch (Exception e) when (e is ValidationException or ConflictException or NotFoundException)
            {
                return (OutcomeOf(e), e);
            }
        }

        private static RuleChangeOutcome? OutcomeOf(Exception e) => (e.GetType() == typeof(ConflictException), e.Message) switch
        {
            (false, Messages.PurchaseOrderRuleTypeFixed) => RuleChangeOutcome.PurchaseOrderRuleTypeFixed,
            (false, Messages.SalesOrderRuleTypeFixed) => RuleChangeOutcome.SalesOrderRuleTypeFixed,
            (true, Messages.PurchaseOrderLastRule) => RuleChangeOutcome.LastPurchaseOrderRule,
            (true, Messages.SalesOrderLastRule) => RuleChangeOutcome.LastSalesOrderRule,
            _ => null, // a refusal that is not one of the four: the module checks
        };
    }

    private sealed class Switches(bool purchasing, bool sales) : IModuleSwitches
    {
        public Task<bool> IsEnabledAsync(string code, CancellationToken ct) => Task.FromResult(code switch { ModuleCodes.Purchasing => purchasing, ModuleCodes.Sales => sales, _ => false });
        public Task<IReadOnlyList<string>> EnabledAsync(CancellationToken ct) => throw new NotSupportedException();
        public void Invalidate() { }
    }

    private sealed class Locker(List<string> journal) : ITransactionLocker
    {
        public Task LockModulesAsync(CancellationToken ct)
        {
            journal.Add(Lock);
            return Task.CompletedTask;
        }

        public Task LockStockAsync(int warehouseId, IReadOnlyCollection<int> productIds, CancellationToken ct) => throw new NotSupportedException();
        public Task LockDocumentSequenceAsync(string prefix, int yearMonth, int warehouseId, CancellationToken ct) => throw new NotSupportedException();
        public Task LockRolesAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task LockModulesSharedAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task LockPurchaseOrderAsync(int orderId, CancellationToken ct) => throw new NotSupportedException();
        public Task LockSalesOrderAsync(int orderId, CancellationToken ct) => throw new NotSupportedException();
        public Task LockReturnOriginAsync(int documentId, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class Work(List<string> journal) : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken ct = default)
        {
            journal.Add(Save);
            return Task.FromResult(1);
        }

        public Task<T> ExecuteAsync<T>(Func<Task<T>> work, CancellationToken ct = default) => work();
    }

    /// <summary>Holds the rules and answers the two reads the handler makes by running its own specifications over them, so "active and this type" is the real filter.</summary>
    private sealed class Store(IEnumerable<ApprovalRule> rules, List<string> journal) : IRepository<ApprovalRule>
    {
        private readonly List<ApprovalRule> _rules = rules.ToList();

        public Task<ApprovalRule?> FirstOrDefaultAsync(ISpecification<ApprovalRule> specification, CancellationToken cancellationToken = default) => Task.FromResult(specification.Evaluate(_rules).FirstOrDefault());

        public Task<int> CountAsync(ISpecification<ApprovalRule> specification, CancellationToken cancellationToken = default)
        {
            journal.Add(Count);
            return Task.FromResult(specification.Evaluate(_rules).Count());
        }

        public void Add(ApprovalRule entity) => throw new NotSupportedException();
        public void AddRange(IEnumerable<ApprovalRule> entities) => throw new NotSupportedException();
        public void Remove(ApprovalRule entity) => throw new NotSupportedException();
        public void RemoveRange(IEnumerable<ApprovalRule> entities) => throw new NotSupportedException();
        public Task<List<ApprovalRule>> ListAsync(ISpecification<ApprovalRule> specification, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<List<ApprovalRule>> ListAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<List<TResult>> ListAsync<TResult>(ISpecification<ApprovalRule, TResult> specification, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ApprovalRule?> GetByIdAsync<TId>(TId id, CancellationToken cancellationToken = default) where TId : notnull => throw new NotSupportedException();
        public Task<TResult?> FirstOrDefaultAsync<TResult>(ISpecification<ApprovalRule, TResult> specification, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ApprovalRule?> SingleOrDefaultAsync(ISingleResultSpecification<ApprovalRule> specification, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<TResult?> SingleOrDefaultAsync<TResult>(ISingleResultSpecification<ApprovalRule, TResult> specification, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<int> CountAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> AnyAsync(ISpecification<ApprovalRule> specification, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> AnyAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<ApprovalRule> AsAsyncEnumerable(ISpecification<ApprovalRule> specification) => throw new NotSupportedException();
    }
}
