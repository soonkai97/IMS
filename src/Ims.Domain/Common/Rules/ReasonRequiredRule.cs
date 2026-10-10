namespace Ims.Domain;

/// <summary>
/// Rejecting, cancelling and reversing must say why (BR-02, OUT-02). A reason is at least <paramref name="minLength"/> characters once
/// trimmed, so "x" or spaces cannot satisfy the audit trail.
/// </summary>
public sealed class ReasonRequiredRule(string? reason, string message, int minLength = 1) : IBusinessRule
{
    public string Message => message;
    public bool IsBroken() => string.IsNullOrWhiteSpace(reason) || reason.Trim().Length < minLength;
}
