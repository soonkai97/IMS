namespace Ims.Domain.Tests.Approvals;

public class ApprovalPolicyTests
{
    private static ApprovalRule Rule(string role, decimal amount = 100m, DocumentType type = DocumentType.Receipt, bool active = true) =>
        ApprovalRule.Create(type, amount, null, role, active);

    // ----- Role rank: Supervisor < Owner < Admin -----
    [Fact]
    public void RankOf_OrdersSupervisorBelowOwnerBelowAdmin()
    {
        Assert.True(ApprovalPolicy.RankOf(Roles.Supervisor) < ApprovalPolicy.RankOf(Roles.Owner));
        Assert.True(ApprovalPolicy.RankOf(Roles.Owner) < ApprovalPolicy.RankOf(Roles.Admin));
    }

    [Theory]
    [InlineData(Roles.WarehouseStaff)]
    [InlineData(Roles.Clerk)]
    [InlineData("Nobody")]
    public void RankOf_RolesWithoutApprovalAuthority_RankBelowSupervisor(string role)
    {
        Assert.True(ApprovalPolicy.RankOf(role) < ApprovalPolicy.RankOf(Roles.Supervisor));
    }

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
        Assert.Equal(expected, ApprovalPolicy.RequiresApproval([Rule(Roles.Supervisor)], DocumentType.Receipt, 1m, (decimal)amount));
    }

    [Fact]
    public void RequiresApproval_QuantityThreshold_Triggers()
    {
        var rule = ApprovalRule.Create(DocumentType.Issue, null, 20m, Roles.Supervisor);

        Assert.True(ApprovalPolicy.RequiresApproval([rule], DocumentType.Issue, 20m, 0m));
        Assert.False(ApprovalPolicy.RequiresApproval([rule], DocumentType.Issue, 19m, 0m));
    }

    [Fact]
    public void RequiresApproval_InactiveRulesAreIgnored()
    {
        Assert.False(ApprovalPolicy.RequiresApproval([Rule(Roles.Supervisor, active: false)], DocumentType.Receipt, 1m, 5000m));
    }

    [Fact]
    public void RequiresApproval_RuleForAnotherDocumentTypeIsIgnored()
    {
        Assert.False(ApprovalPolicy.RequiresApproval([Rule(Roles.Supervisor, type: DocumentType.Issue)], DocumentType.Receipt, 1m, 5000m));
    }

    [Fact]
    public void RequiresApproval_AnyOneMatchingRuleIsEnough()
    {
        ApprovalRule[] rules = [Rule(Roles.Supervisor, amount: 10_000m), Rule(Roles.Owner, amount: 100m)];

        Assert.True(ApprovalPolicy.RequiresApproval(rules, DocumentType.Receipt, 1m, 500m));
    }

    // ----- CanApprove -----
    [Theory]
    [InlineData(Roles.Supervisor, true)]
    [InlineData(Roles.Owner, true)]
    [InlineData(Roles.Admin, true)]
    [InlineData(Roles.WarehouseStaff, false)]
    [InlineData(Roles.Clerk, false)]
    public void CanApprove_NoRuleFired_AnySupervisorOrHigherSuffices(string role, bool expected)
    {
        Assert.Equal(expected, ApprovalPolicy.CanApprove(role, [], DocumentType.Receipt, 1m, 1m));
    }

    [Theory]
    [InlineData(Roles.Supervisor, false)]
    [InlineData(Roles.Owner, true)]
    [InlineData(Roles.Admin, true)]
    public void CanApprove_RuleRequiresOwner_SupervisorIsTooLow(string approver, bool expected)
    {
        var rules = new[] { Rule(Roles.Owner) };

        Assert.Equal(expected, ApprovalPolicy.CanApprove(approver, rules, DocumentType.Receipt, 1m, 500m));
    }

    [Theory]
    [InlineData(Roles.Supervisor, false)]
    [InlineData(Roles.Owner, false)]
    [InlineData(Roles.Admin, true)]
    public void CanApprove_RuleRequiresAdmin_OnlyAdmin(string approver, bool expected)
    {
        var rules = new[] { Rule(Roles.Admin) };

        Assert.Equal(expected, ApprovalPolicy.CanApprove(approver, rules, DocumentType.Receipt, 1m, 500m));
    }

    [Fact]
    public void CanApprove_ApproverMustOutrankEveryRuleThatFired()
    {
        var rules = new[] { Rule(Roles.Supervisor, amount: 100m), Rule(Roles.Owner, amount: 1000m) };

        Assert.False(ApprovalPolicy.CanApprove(Roles.Supervisor, rules, DocumentType.Receipt, 1m, 2000m)); // both fire
        Assert.True(ApprovalPolicy.CanApprove(Roles.Supervisor, rules, DocumentType.Receipt, 1m, 500m));   // only the supervisor rule fires
        Assert.True(ApprovalPolicy.CanApprove(Roles.Owner, rules, DocumentType.Receipt, 1m, 2000m));
    }

    [Fact]
    public void CanApprove_RulesThatDidNotFireDoNotRaiseTheBar()
    {
        var rules = new[] { Rule(Roles.Admin, amount: 1_000_000m) };

        Assert.True(ApprovalPolicy.CanApprove(Roles.Supervisor, rules, DocumentType.Receipt, 1m, 10m));
    }

    [Fact]
    public void CanApprove_InactiveHighRuleDoesNotRaiseTheBar()
    {
        var rules = new[] { Rule(Roles.Admin, amount: 1m, active: false) };

        Assert.True(ApprovalPolicy.CanApprove(Roles.Supervisor, rules, DocumentType.Receipt, 1m, 10m));
    }
}
