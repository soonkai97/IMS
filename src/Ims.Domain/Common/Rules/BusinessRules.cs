namespace Ims.Domain;

public static class BusinessRules
{
    /// <exception cref="DomainException">The rule is broken.</exception>
    public static void Ensure(IBusinessRule rule)
    {
        if (rule.IsBroken()) throw new DomainException(rule.Message);
    }
}
