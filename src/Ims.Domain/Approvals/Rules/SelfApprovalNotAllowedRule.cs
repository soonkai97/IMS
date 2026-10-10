namespace Ims.Domain;

/// <summary>BR-07: segregation of duties. The person who submitted a document cannot approve (or reject) it unless the customer allows it.</summary>
public sealed class SelfApprovalNotAllowedRule(int? submittedBy, int reviewerId, bool allowSelfApproval, string message) : IBusinessRule
{
    public string Message => message;
    public bool IsBroken() => !allowSelfApproval && submittedBy == reviewerId;
}
