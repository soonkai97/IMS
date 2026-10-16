namespace Ims.Domain.Tests.Approvals;

public class ApprovalRuleTests
{
    private static ApprovalRule AmountRule(decimal amount, DocumentType type = DocumentType.Receipt, bool active = true) =>
        ApprovalRule.Create(type, amount, null, Roles.Supervisor, active);

    private static ApprovalRule QtyRule(decimal qty, DocumentType type = DocumentType.Receipt, bool active = true) =>
        ApprovalRule.Create(type, null, qty, Roles.Supervisor, active);

    // ----- Create / Update guards -----
    [Fact]
    public void Create_StoresAllFields()
    {
        var rule = ApprovalRule.Create(DocumentType.Issue, 1000m, 50m, Roles.Owner, isActive: false);

        Assert.Equal(DocumentType.Issue, rule.DocType);
        Assert.Equal(1000m, rule.AmountThreshold);
        Assert.Equal(50m, rule.QtyThreshold);
        Assert.Equal(Roles.Owner, rule.RoleRequired);
        Assert.False(rule.IsActive);
    }

    [Fact]
    public void Create_WithoutAnyThreshold_Throws()
    {
        var ex = Assert.Throws<DomainException>(() => ApprovalRule.Create(DocumentType.Receipt, null, null, Roles.Supervisor));

        Assert.Equal(Messages.ThresholdRequired, ex.Message);
    }

    [Theory]
    [InlineData(-1.0, null)]
    [InlineData(null, -1.0)]
    [InlineData(10.0, -0.5)]
    public void Create_NegativeThreshold_Throws(double? amount, double? qty)
    {
        var ex = Assert.Throws<DomainException>(() =>
            ApprovalRule.Create(DocumentType.Receipt, (decimal?)amount, (decimal?)qty, Roles.Supervisor));

        Assert.Equal(Messages.ThresholdNegative, ex.Message);
    }

    [Fact]
    public void Create_UnknownRole_Throws()
    {
        var ex = Assert.Throws<DomainException>(() => ApprovalRule.Create(DocumentType.Receipt, 1m, null, "Janitor"));

        Assert.Equal(Messages.UnknownRole, ex.Message);
    }

    [Fact]
    public void Create_ZeroThreshold_IsAllowedAndMatchesEverything()
    {
        var rule = AmountRule(0m);

        Assert.True(rule.Matches(DocumentType.Receipt, 1m, 0m));
    }

    [Fact]
    public void Update_WithInvalidValues_LeavesTheRuleUntouched()
    {
        var rule = ApprovalRule.Create(DocumentType.Receipt, 100m, null, Roles.Supervisor);

        Assert.Throws<DomainException>(() => rule.Update(DocumentType.Issue, null, null, Roles.Owner, false));

        Assert.Equal(DocumentType.Receipt, rule.DocType);
        Assert.Equal(100m, rule.AmountThreshold);
        Assert.Equal(Roles.Supervisor, rule.RoleRequired);
        Assert.True(rule.IsActive);
    }

    [Fact]
    public void Update_ChangesAllFields()
    {
        var rule = AmountRule(100m);

        rule.Update(DocumentType.Issue, null, 20m, Roles.Admin, false);

        Assert.Equal(DocumentType.Issue, rule.DocType);
        Assert.Null(rule.AmountThreshold);
        Assert.Equal(20m, rule.QtyThreshold);
        Assert.Equal(Roles.Admin, rule.RoleRequired);
        Assert.False(rule.IsActive);
    }

    [Fact]
    public void EverySaveWritesTheTierWithTheRole_OwnerAndAdminAreLarge_TheOthersStandard()
    {
        var rule = ApprovalRule.Create(DocumentType.Receipt, 100m, null, Roles.Owner);
        Assert.Equal(ApproverTier.Large, rule.Tier);

        foreach (var (role, tier) in new[]
                 {
                     (Roles.Supervisor, ApproverTier.Standard), (Roles.Admin, ApproverTier.Large), (Roles.Clerk, ApproverTier.Standard),
                     (Roles.WarehouseStaff, ApproverTier.Standard), (Roles.Owner, ApproverTier.Large)
                 })
        {
            rule.Update(DocumentType.Receipt, 100m, null, role, true);
            Assert.Equal(tier, rule.Tier);
        }
    }

    [Fact]
    public void ARejectedUpdate_LeavesTheTierAsItWas()
    {
        var rule = ApprovalRule.Create(DocumentType.Receipt, 100m, null, Roles.Owner);

        Assert.Throws<DomainException>(() => rule.Update(DocumentType.Receipt, 100m, null, "Janitor", true));

        Assert.Equal(ApproverTier.Large, rule.Tier);
    }

    // ----- Matches -----
    [Theory]
    [InlineData(99.99, false)]
    [InlineData(100.0, true)]   // "at or above"
    [InlineData(100.01, true)]
    public void Matches_AmountThreshold_IsInclusive(double amount, bool expected)
    {
        Assert.Equal(expected, AmountRule(100m).Matches(DocumentType.Receipt, 1m, (decimal)amount));
    }

    [Theory]
    [InlineData(49.0, false)]
    [InlineData(50.0, true)]
    [InlineData(51.0, true)]
    public void Matches_QtyThreshold_IsInclusive(double qty, bool expected)
    {
        Assert.Equal(expected, QtyRule(50m).Matches(DocumentType.Receipt, (decimal)qty, 0m));
    }

    [Theory]
    [InlineData(10, 500, false)]  // neither
    [InlineData(10, 1500, true)]  // amount only
    [InlineData(80, 500, true)]   // qty only
    [InlineData(80, 1500, true)]  // both
    public void Matches_BothThresholds_EitherOneTriggers(double qty, double amount, bool expected)
    {
        var rule = ApprovalRule.Create(DocumentType.Receipt, 1000m, 50m, Roles.Supervisor);

        Assert.Equal(expected, rule.Matches(DocumentType.Receipt, (decimal)qty, (decimal)amount));
    }

    [Fact]
    public void Matches_InactiveRule_NeverMatches()
    {
        Assert.False(AmountRule(1m, active: false).Matches(DocumentType.Receipt, 1000m, 1000m));
    }

    [Fact]
    public void Matches_OtherDocumentType_DoesNotMatch()
    {
        Assert.False(AmountRule(1m, DocumentType.Issue).Matches(DocumentType.Receipt, 1000m, 1000m));
    }

    [Fact]
    public void Matches_AmountOnlyRule_IgnoresQuantity()
    {
        Assert.False(AmountRule(1000m).Matches(DocumentType.Receipt, 1_000_000m, 10m));
    }

    [Fact]
    public void Matches_QtyOnlyRule_IgnoresAmount()
    {
        Assert.False(QtyRule(1000m).Matches(DocumentType.Receipt, 10m, 1_000_000m));
    }

    // ----- Triggers: the plain-words "why" behind an approval -----
    [Fact]
    public void Triggers_ReportsEachThresholdTheDocumentReaches()
    {
        var rule = ApprovalRule.Create(DocumentType.Receipt, 5000m, 100m, Roles.Owner);

        var triggers = rule.Triggers(DocumentType.Receipt, totalQty: 150m, totalAmount: 6000m).ToList();

        Assert.Equal(
            [new ApprovalTrigger(ApprovalMetric.Amount, 5000m, 6000m, Roles.Owner), new ApprovalTrigger(ApprovalMetric.Quantity, 100m, 150m, Roles.Owner)],
            triggers);
    }

    [Fact]
    public void Triggers_ReportsOnlyTheThresholdThatIsReached()
    {
        var rule = ApprovalRule.Create(DocumentType.Issue, 5000m, 100m, Roles.Supervisor);

        var trigger = Assert.Single(rule.Triggers(DocumentType.Issue, totalQty: 10m, totalAmount: 5000m));

        Assert.Equal(ApprovalMetric.Amount, trigger.Metric);
    }

    [Fact]
    public void Triggers_IsEmptyWhenTheRuleDoesNotMatch()
    {
        var rule = AmountRule(1000m);

        Assert.Empty(rule.Triggers(DocumentType.Receipt, 1m, 999m));
        Assert.Empty(rule.Triggers(DocumentType.Issue, 1m, 5000m));
        Assert.Empty(AmountRule(1000m, active: false).Triggers(DocumentType.Receipt, 1m, 5000m));
    }
}
