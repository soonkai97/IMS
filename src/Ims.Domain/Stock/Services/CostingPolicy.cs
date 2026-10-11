namespace Ims.Domain;

/// <summary>BR-05: how document costs are expressed. Costs are entered per entered unit and stored per base unit.</summary>
public static class CostingPolicy
{
    /// <summary>
    /// Receipts take the entered cost, converted to a cost per base unit. Issues start at zero: they are booked at the
    /// average cost in force when they post.
    /// </summary>
    public static decimal BaseUnitCost(DocumentType type, decimal? enteredCostPerUnit, decimal enteredQty, decimal baseQty)
    {
        if (type != DocumentType.Receipt || enteredCostPerUnit is not { } cost || baseQty <= 0) return 0m;
        return Math.Round(cost * enteredQty / baseQty, Limits.CostDecimals);
    }
}
