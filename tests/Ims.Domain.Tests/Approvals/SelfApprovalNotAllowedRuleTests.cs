namespace Ims.Domain.Tests.Approvals;

public class SelfApprovalNotAllowedRuleTests
{
    [Theory]
    [InlineData(5, 5, false, true)]   // same person, not allowed -> broken
    [InlineData(5, 5, true, false)]   // same person, allowed by setting
    [InlineData(5, 6, false, false)]  // different person
    [InlineData(5, 6, true, false)]
    [InlineData(null, 6, false, false)] // submitter unknown
    public void IsBroken_FollowsSegregationOfDuties(int? submittedBy, int approver, bool allowSelf, bool broken)
    {
        var rule = new SelfApprovalNotAllowedRule(submittedBy, approver, allowSelf, Messages.SelfApproval);

        Assert.Equal(broken, rule.IsBroken());
        Assert.Equal(Messages.SelfApproval, rule.Message);
    }
}
