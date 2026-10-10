namespace Ims.Domain;

/// <summary>BR-07: segregation of duties. The person who submitted a document cannot approve it (unless the customer allows it).</summary>
public sealed class SelfApprovalNotAllowedRule(int? submittedBy, int approverId, bool allowSelfApproval) : IBusinessRule
{
    public string Message => Messages.SelfApproval;
    public bool IsBroken() => !allowSelfApproval && submittedBy == approverId;
}
