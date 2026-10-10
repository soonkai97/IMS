namespace Ims.Domain;

/// <summary>BR-04: quantities are stored in the base unit, so it cannot change once the product has stock history.</summary>
public sealed class BaseUnitLockedRule(int currentUomId, int newUomId, bool hasStockMovements) : IBusinessRule
{
    public string Message => Messages.BaseUomLocked;
    public bool IsBroken() => currentUomId != newUomId && hasStockMovements;
}
