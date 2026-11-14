namespace Ims.Domain.Tests.Approvals;

/// <summary>
/// DDD-10: the two rules that span the approval rules of one document type, decided in the Domain from facts (the rule, the type and state it is being given, whether its module is on,
/// how many active rules the type has). The table is the one the handler's characterization test runs (<see cref="ApprovalRuleChangeTable"/>), so both give the same answers.
/// </summary>
public class ApprovalRuleSetPolicyTests
{
    private static ApprovalRule RuleOf(ApprovalRuleChangeTable.Case c) => ApprovalRule.Create(c.Type, 100m, null, ApproverTier.Standard, c.Active);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void EveryOldTypeNewTypeActiveStateAndCount_GivesTheRefusalTheTableWrites(bool moduleOn)
    {
        var wrong = new List<string>();
        var count = 0;
        foreach (var c in ApprovalRuleChangeTable.All())
        {
            count++;
            var actual = ApprovalRuleSetPolicy.Refusal(RuleOf(c), c.NewType, c.NewActive, moduleOn, c.ActiveOfType);

            var expected = ApprovalRuleChangeTable.Expected(c, moduleOn);
            if (actual.ToString() != expected.ToString()) wrong.Add($"{c}, module {(moduleOn ? "on" : "off")}: expected {expected}, got {actual}");
        }

        Assert.True(count > 400, $"the table is the whole grid ({count} cases)");
        Assert.True(wrong.Count == 0, $"{wrong.Count} of {count} differ:{Environment.NewLine}{string.Join(Environment.NewLine, wrong.Take(15))}");
    }

    [Fact]
    public void TheGridReachesEveryRefusal()
    {
        var reached = ApprovalRuleChangeTable.All().Select(c => ApprovalRuleSetPolicy.Refusal(RuleOf(c), c.NewType, c.NewActive, moduleOn: true, c.ActiveOfType)).Distinct().Order().ToList();

        Assert.Equal(Enum.GetValues<ApprovalRuleRefusalReason>().Order(), reached);
        Assert.Equal(Enum.GetNames<RuleChangeOutcome>(), Enum.GetNames<ApprovalRuleRefusalReason>()); // the table names every refusal the policy has, in the same words
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TheCountIsAskedForOnlyWhenItCanChangeTheAnswer(bool moduleOn)
    {
        foreach (var c in ApprovalRuleChangeTable.All())
        {
            var needs = ApprovalRuleSetPolicy.NeedsActiveRuleCount(RuleOf(c), c.NewType, c.NewActive, moduleOn);

            Assert.True(needs == ApprovalRuleChangeTable.NeedsCount(c, moduleOn), $"{c}, module {(moduleOn ? "on" : "off")}: needs the count = {needs}");
            if (!needs) // whatever count a caller passes in that case, the answer is the same
                Assert.Equal(ApprovalRuleSetPolicy.Refusal(RuleOf(c), c.NewType, c.NewActive, moduleOn, 0), ApprovalRuleSetPolicy.Refusal(RuleOf(c), c.NewType, c.NewActive, moduleOn, 99));
        }
    }

    [Fact]
    public void TheCountIncludesTheRuleBeingSwitchedOff_SoOneOrFewerRefusesAndTwoAllows()
    {
        var rule = ApprovalRule.Create(DocumentType.SalesOrder, 100m, null, ApproverTier.Standard);

        Assert.Equal(ApprovalRuleRefusalReason.LastSalesOrderRule, ApprovalRuleSetPolicy.Refusal(rule, DocumentType.SalesOrder, newIsActive: false, moduleOn: true, activeRulesOfType: 1));
        Assert.Equal(ApprovalRuleRefusalReason.None, ApprovalRuleSetPolicy.Refusal(rule, DocumentType.SalesOrder, newIsActive: false, moduleOn: true, activeRulesOfType: 2));
    }

    [Fact]
    public void SwitchingOffARuleOfAStockDocumentType_IsNeverRefused()
    {
        var rule = ApprovalRule.Create(DocumentType.Receipt, 100m, null, ApproverTier.Standard);

        Assert.Equal(ApprovalRuleRefusalReason.None, ApprovalRuleSetPolicy.Refusal(rule, DocumentType.Receipt, newIsActive: false, moduleOn: true, activeRulesOfType: 1));
        Assert.False(ApprovalRuleSetPolicy.NeedsActiveRuleCount(rule, DocumentType.Receipt, newIsActive: false, moduleOn: true));
    }

    [Fact]
    public void ARuleThatIsAlreadyOff_CanBeEditedWhateverTheCount()
    {
        var rule = ApprovalRule.Create(DocumentType.PurchaseOrder, 100m, null, ApproverTier.Standard, isActive: false);

        Assert.Equal(ApprovalRuleRefusalReason.None, ApprovalRuleSetPolicy.Refusal(rule, DocumentType.PurchaseOrder, newIsActive: false, moduleOn: true, activeRulesOfType: 0));
    }

    [Fact]
    public void TheTypeRuleComesBeforeTheLastRuleRule_AndNeedsNoCount()
    {
        // A last active purchase order rule moved to another type and switched off in the same edit: the move is what is refused (a 400), not the switch-off (a 409), and nothing is counted.
        var rule = ApprovalRule.Create(DocumentType.PurchaseOrder, 100m, null, ApproverTier.Standard);

        Assert.Equal(ApprovalRuleRefusalReason.PurchaseOrderRuleTypeFixed, ApprovalRuleSetPolicy.Refusal(rule, DocumentType.Receipt, newIsActive: false, moduleOn: true, activeRulesOfType: 1));
        Assert.False(ApprovalRuleSetPolicy.NeedsActiveRuleCount(rule, DocumentType.Receipt, newIsActive: false, moduleOn: true));
    }

    [Theory]
    [InlineData(DocumentType.Receipt, DocumentType.PurchaseOrder, ApprovalRuleRefusalReason.PurchaseOrderRuleTypeFixed)]
    [InlineData(DocumentType.PurchaseOrder, DocumentType.Issue, ApprovalRuleRefusalReason.PurchaseOrderRuleTypeFixed)]
    [InlineData(DocumentType.Issue, DocumentType.SalesOrder, ApprovalRuleRefusalReason.SalesOrderRuleTypeFixed)]
    [InlineData(DocumentType.SalesOrder, DocumentType.Adjust, ApprovalRuleRefusalReason.SalesOrderRuleTypeFixed)]
    [InlineData(DocumentType.PurchaseOrder, DocumentType.SalesOrder, ApprovalRuleRefusalReason.SalesOrderRuleTypeFixed)] // either side a sales order names the sales order
    [InlineData(DocumentType.SalesOrder, DocumentType.PurchaseOrder, ApprovalRuleRefusalReason.SalesOrderRuleTypeFixed)]
    [InlineData(DocumentType.Receipt, DocumentType.Issue, ApprovalRuleRefusalReason.None)] // stock documents may be moved between types
    [InlineData(DocumentType.Transfer, DocumentType.Adjust, ApprovalRuleRefusalReason.None)]
    public void ARuleKeepsItsOrderType_AndNoOtherRuleBecomesOne(DocumentType from, DocumentType to, ApprovalRuleRefusalReason expected)
    {
        var rule = ApprovalRule.Create(from, 100m, null, ApproverTier.Standard);

        Assert.Equal(expected, ApprovalRuleSetPolicy.Refusal(rule, to, newIsActive: true, moduleOn: true, activeRulesOfType: 5));
    }
}
