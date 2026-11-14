using Ardalis.Specification;
using Ims.Application;
using Ims.Application.Features.Documents;

namespace Ims.Domain.Tests.Approvals;

/// <summary>
/// DDD-07, characterization: <c>ApprovalEvaluator.ReviewRefusalAsync</c> as it is on <c>main</c> before the decision moves into <c>ApprovalPolicy.ReviewRefusal</c>.
/// Green before the change and green after it: the same sentence for every input, and the same reads of the rules (none when routing or a return already decides).
/// The 403 comes from the handlers (<c>ForbiddenException(refusal)</c>), which these tests do not touch; <c>Ims.Api.Tests/Tests/Rbac</c> proves the status.
/// </summary>
public class ApprovalEvaluatorReviewRefusalTests
{
    private static string? SentenceOf(ReviewOutcome outcome) => outcome switch
    {
        ReviewOutcome.None => null,
        ReviewOutcome.OwnerReviewsLargeOnly => Messages.OwnerReviewsLargeOnly,
        ReviewOutcome.AboveApprovalLimit => Messages.AboveApprovalLimit,
        _ => throw new ArgumentOutOfRangeException(nameof(outcome)),
    };

    [Fact]
    public async Task EveryCombinationOfRightsTierRulesTypeSizeAndReturn_GivesTheSameSentenceOrNone()
    {
        var wrong = new List<string>();
        var count = 0;
        foreach (var c in ReviewRefusalTable.All())
        {
            count++;
            var evaluator = new ApprovalEvaluator(new RulesRepository(c.Rules.Rules.Select(r => r.ToEntity())), new Caller(c.Caller.Rights()));

            var actual = await evaluator.ReviewRefusalAsync(c.Type, c.IsLarge, c.Qty, c.Amount, CancellationToken.None, c.IsReturn);

            var expected = SentenceOf(ReviewRefusalTable.Expected(c));
            if (actual != expected) wrong.Add($"{c}: expected [{expected ?? "null"}], got [{actual ?? "null"}]");
        }

        Assert.True(count > 5000, $"the table is the whole grid ({count} cases)");
        Assert.Equal(Enum.GetValues<ReviewOutcome>().Length, ReviewRefusalTable.All().Select(ReviewRefusalTable.Expected).Distinct().Count()); // the grid reaches every outcome
        Assert.True(wrong.Count == 0, $"{wrong.Count} of {count} differ:{Environment.NewLine}{string.Join(Environment.NewLine, wrong.Take(15))}");
    }

    [Fact]
    public async Task TheTwoSentencesAreTheOnesTheApiAnswersWith()
    {
        Assert.Equal("Owners review only large documents. A supervisor reviews this one.", Messages.OwnerReviewsLargeOnly);
        Assert.Equal("This document is above your approval limit. A higher role must approve it.", Messages.AboveApprovalLimit);

        var owner = new ApprovalEvaluator(new RulesRepository([]), new Caller(new ActorRights(1, EffectivePermissions.Compute([Permissions.DocumentApproveLarge]))));
        var supervisor = new ApprovalEvaluator(new RulesRepository([]), new Caller(new ActorRights(1, EffectivePermissions.Compute([Permissions.DocumentApprove]))));

        Assert.Equal(Messages.OwnerReviewsLargeOnly, await owner.ReviewRefusalAsync(DocumentType.Receipt, isLarge: false, 1m, 1m, CancellationToken.None));
        Assert.Equal(Messages.AboveApprovalLimit, await supervisor.ReviewRefusalAsync(DocumentType.Receipt, isLarge: true, 1m, 1m, CancellationToken.None));
        Assert.Null(await owner.ReviewRefusalAsync(DocumentType.Receipt, isLarge: true, 1m, 1m, CancellationToken.None));
    }

    [Theory]
    [InlineData(new[] { Permissions.DocumentApprove }, false, false, 1)]      // may review: the rules in force are read
    [InlineData(new[] { Permissions.DocumentApprove }, false, true, 0)]       // a return has no limit: they are not read
    [InlineData(new[] { Permissions.DocumentApprove }, true, false, 0)]       // routed away by the stored tier: not read
    [InlineData(new[] { Permissions.DocumentApproveLarge }, false, false, 0)] // the Owner is routed away from a standard document: not read
    [InlineData(new string[0], false, false, 0)]
    [InlineData(new[] { Permissions.DocumentApprove, Permissions.DocumentApproveLarge }, true, false, 1)]
    public async Task TheRulesInForceAreReadOnlyWhenTheyCanChangeTheAnswer(string[] held, bool isLarge, bool isReturn, int reads)
    {
        var repository = new RulesRepository([]);
        var evaluator = new ApprovalEvaluator(repository, new Caller(new ActorRights(1, EffectivePermissions.Compute(held))));

        await evaluator.ReviewRefusalAsync(DocumentType.Receipt, isLarge, 1m, 1m, CancellationToken.None, isReturn);

        Assert.Equal(reads, repository.Reads);
    }

    private sealed class Caller(ActorRights rights) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public int UserId => rights.UserId;
        public string Username => "tester";
        public string Role => "n/a";
        public IReadOnlyCollection<int> WarehouseIds => [];
        public bool CanAccessWarehouse(int warehouseId) => true;
        public bool AllWarehouses => true;
        public ActorRights Rights() => rights;
    }

    /// <summary>Holds the rules and answers the one read the evaluator makes by running its own specification over them, so "active and this type" is the real filter.</summary>
    private sealed class RulesRepository(IEnumerable<ApprovalRule> rules) : IReadRepository<ApprovalRule>
    {
        private readonly List<ApprovalRule> _rules = rules.ToList();
        public int Reads { get; private set; }

        public Task<List<ApprovalRule>> ListAsync(ISpecification<ApprovalRule> specification, CancellationToken cancellationToken = default)
        {
            Reads++;
            return Task.FromResult(specification.Evaluate(_rules).ToList());
        }

        public Task<List<ApprovalRule>> ListAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<List<TResult>> ListAsync<TResult>(ISpecification<ApprovalRule, TResult> specification, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ApprovalRule?> GetByIdAsync<TId>(TId id, CancellationToken cancellationToken = default) where TId : notnull => throw new NotSupportedException();
        public Task<ApprovalRule?> FirstOrDefaultAsync(ISpecification<ApprovalRule> specification, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<TResult?> FirstOrDefaultAsync<TResult>(ISpecification<ApprovalRule, TResult> specification, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ApprovalRule?> SingleOrDefaultAsync(ISingleResultSpecification<ApprovalRule> specification, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<TResult?> SingleOrDefaultAsync<TResult>(ISingleResultSpecification<ApprovalRule, TResult> specification, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<int> CountAsync(ISpecification<ApprovalRule> specification, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<int> CountAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> AnyAsync(ISpecification<ApprovalRule> specification, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> AnyAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<ApprovalRule> AsAsyncEnumerable(ISpecification<ApprovalRule> specification) => throw new NotSupportedException();
    }
}
