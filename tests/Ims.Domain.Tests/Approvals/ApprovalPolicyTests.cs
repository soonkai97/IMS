namespace Ims.Domain.Tests.Approvals;

public class ApprovalPolicyTests
{
    private static ApprovalRule Rule(ApproverTier tier, decimal amount = 100m, DocumentType type = DocumentType.Receipt, bool active = true) =>
        ApprovalRule.Create(type, amount, null, tier, active);

    // ----- RequiresApproval -----
    [Fact]
    public void RequiresApproval_NoRules_IsFalse()
    {
        Assert.False(ApprovalPolicy.RequiresApproval([], DocumentType.Receipt, 1000m, 1_000_000m));
    }

    [Theory]
    [InlineData(99.0, false)]
    [InlineData(100.0, true)]
    [InlineData(5000.0, true)]
    public void RequiresApproval_FollowsTheAmountThreshold(double amount, bool expected)
    {
        Assert.Equal(expected, ApprovalPolicy.RequiresApproval([Rule(ApproverTier.Standard)], DocumentType.Receipt, 1m, (decimal)amount));
    }

    [Fact]
    public void RequiresApproval_QuantityThreshold_Triggers()
    {
        var rule = ApprovalRule.Create(DocumentType.Issue, null, 20m, ApproverTier.Standard);

        Assert.True(ApprovalPolicy.RequiresApproval([rule], DocumentType.Issue, 20m, 0m));
        Assert.False(ApprovalPolicy.RequiresApproval([rule], DocumentType.Issue, 19m, 0m));
    }

    [Fact]
    public void RequiresApproval_InactiveRulesAreIgnored()
    {
        Assert.False(ApprovalPolicy.RequiresApproval([Rule(ApproverTier.Standard, active: false)], DocumentType.Receipt, 1m, 5000m));
    }

    [Fact]
    public void RequiresApproval_RuleForAnotherDocumentTypeIsIgnored()
    {
        Assert.False(ApprovalPolicy.RequiresApproval([Rule(ApproverTier.Standard, type: DocumentType.Issue)], DocumentType.Receipt, 1m, 5000m));
    }

    [Fact]
    public void RequiresApproval_AnyOneMatchingRuleIsEnough()
    {
        ApprovalRule[] rules = [Rule(ApproverTier.Standard, amount: 10_000m), Rule(ApproverTier.Large, amount: 100m)];

        Assert.True(ApprovalPolicy.RequiresApproval(rules, DocumentType.Receipt, 1m, 500m));
    }

    // ----- IsLarge: a rule of the Large tier fired -----
    [Fact]
    public void IsLarge_NoRules_IsFalse()
    {
        Assert.False(ApprovalPolicy.IsLarge([], DocumentType.Receipt, 1m, 1_000_000m));
    }

    [Fact]
    public void IsLarge_OnlyAStandardRuleFired_IsFalse()
    {
        var rules = new[] { Rule(ApproverTier.Standard, amount: 100m), Rule(ApproverTier.Large, amount: 1000m) };

        Assert.False(ApprovalPolicy.IsLarge(rules, DocumentType.Receipt, 1m, 500m));
    }

    [Fact]
    public void IsLarge_ALargeRuleFired_IsTrue()
    {
        var rules = new[] { Rule(ApproverTier.Standard, amount: 100m), Rule(ApproverTier.Large, amount: 1000m) };

        Assert.True(ApprovalPolicy.IsLarge(rules, DocumentType.Receipt, 1m, 2000m));
    }

    [Fact]
    public void IsLarge_InactiveOrOtherTypeRulesAreIgnored()
    {
        var rules = new[] { Rule(ApproverTier.Large, amount: 1m, active: false), Rule(ApproverTier.Large, amount: 1m, type: DocumentType.Issue) };

        Assert.False(ApprovalPolicy.IsLarge(rules, DocumentType.Receipt, 1m, 10m));
    }

    // ----- MayReview: the reviewer a document is routed to, from the permissions a person holds (PRD 3.1) -----
    private static ActorRights With(params string[] codes) => new(1, EffectivePermissions.Compute(codes));

    [Theory]
    [InlineData(new[] { Permissions.DocumentApprove }, false, true)]
    [InlineData(new[] { Permissions.DocumentApprove }, true, false)]          // an Owner-level document is not the supervisor's (PS-07)
    [InlineData(new[] { Permissions.DocumentApproveLarge }, false, false)]    // the Owner reviews only large documents (EA-02)
    [InlineData(new[] { Permissions.DocumentApproveLarge }, true, true)]
    [InlineData(new[] { Permissions.DocumentApprove, Permissions.DocumentApproveLarge }, false, true)]   // the Admin holds both tiers
    [InlineData(new[] { Permissions.DocumentApprove, Permissions.DocumentApproveLarge }, true, true)]
    [InlineData(new string[0], false, false)]
    [InlineData(new string[0], true, false)]
    public void MayReview_FollowsThePermissionOfTheTier(string[] held, bool isLarge, bool expected)
    {
        Assert.Equal(expected, ApprovalPolicy.MayReview(With(held), isLarge));
    }

    // ----- ReviewRefusal (DDD-07): the same table as the evaluator's characterization test, asked of the policy -----
    [Fact]
    public void ReviewRefusal_EveryCombinationOfRightsTierRulesTypeSizeAndReturn_FollowsTheTable()
    {
        var wrong = new List<string>();
        var count = 0;
        foreach (var c in ReviewRefusalTable.All())
        {
            count++;
            var actual = ApprovalPolicy.ReviewRefusal(c.Caller.Rights(), c.IsLarge, c.Rules.Rules.Select(r => r.ToEntity()), c.Type, c.Qty, c.Amount, c.IsReturn);

            var expected = ReviewRefusalTable.Expected(c);
            if (actual.ToString() != expected.ToString()) wrong.Add($"{c}: expected {expected}, got {actual}");
        }

        Assert.True(count > 5000, $"the table is the whole grid ({count} cases)");
        Assert.True(wrong.Count == 0, $"{wrong.Count} of {count} differ:{Environment.NewLine}{string.Join(Environment.NewLine, wrong.Take(15))}");
    }

    [Fact]
    public void ReviewRefusal_TheRefusalNamesAreTheOnesTheTableUses()
    {
        Assert.Equal(Enum.GetNames<ReviewOutcome>(), Enum.GetNames<ReviewRefusalReason>());
    }

    [Fact]
    public void ReviewRefusal_RulesNotAskedAbout_AreIgnored()
    {
        ApprovalRule[] large = [Rule(ApproverTier.Large, amount: 1m)];

        // routed away: the rules in force do not matter
        Assert.Equal(ReviewRefusalReason.AboveApprovalLimit, ApprovalPolicy.ReviewRefusal(With(), isLarge: false, large, DocumentType.Receipt, 1m, 500m, isReturn: false));
        // a return: the rules in force do not apply to it, a standard approver reviews even when a Large rule would fire
        Assert.Equal(ReviewRefusalReason.None, ApprovalPolicy.ReviewRefusal(With(Permissions.DocumentApprove), isLarge: false, large, DocumentType.Receipt, 1m, 500m, isReturn: true));
    }

    [Theory]
    [InlineData(new[] { Permissions.DocumentApprove }, false, false, true)]      // may review a standard document: the rules in force can still refuse
    [InlineData(new[] { Permissions.DocumentApprove }, false, true, false)]      // a return has no limit
    [InlineData(new[] { Permissions.DocumentApprove }, true, false, false)]      // routed away by the stored tier
    [InlineData(new[] { Permissions.DocumentApproveLarge }, false, false, false)]
    [InlineData(new string[0], true, false, false)]
    [InlineData(new[] { Permissions.DocumentApprove, Permissions.DocumentApproveLarge }, true, false, true)]
    public void NeedsRulesInForce_OnlyWhenRoutingLetsThemThroughAndItIsNotAReturn(string[] held, bool isLarge, bool isReturn, bool expected)
    {
        Assert.Equal(expected, ApprovalPolicy.NeedsRulesInForce(With(held), isLarge, isReturn));
    }
}
