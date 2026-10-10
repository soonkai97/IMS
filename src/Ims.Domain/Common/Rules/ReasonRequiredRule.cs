namespace Ims.Domain;

/// <summary>Rejecting, cancelling and reversing must say why (BR-02, OUT-02).</summary>
public sealed class ReasonRequiredRule(string? reason, string message) : IBusinessRule
{
    public string Message => message;
    public bool IsBroken() => string.IsNullOrWhiteSpace(reason);
}
