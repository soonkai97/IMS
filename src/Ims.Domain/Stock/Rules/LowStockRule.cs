using System.Linq.Expressions;

namespace Ims.Domain;

/// <summary>
/// The one definition of "low stock", written as expressions so the database evaluates it and every screen uses the same one: the low-stock list and its
/// export, the Home count and the menu badge, the stock list's "low only" filter and the <c>isLow</c> flag of a stock row.
/// <para>
/// A product is low in a warehouse when it is ACTIVE and either its on-hand quantity is below zero, or it has a safety level above zero and the on-hand
/// quantity is below it (a quantity equal to the safety level is not low). An active product with a safety level that has never been stocked in any
/// warehouse (it has no balance row) is low in every active warehouse in the caller's scope, with a quantity of zero.
/// </para>
/// </summary>
public static class LowStockRule
{
    /// <summary>A balance row that is low: the first half of the definition.</summary>
    public static readonly Expression<Func<StockBalance, bool>> IsLowBalance =
        b => b.Product.IsActive && (b.QtyOnHand < 0 || (b.Product.SafetyStock > 0 && b.QtyOnHand < b.Product.SafetyStock));

    /// <summary>A product that can be "never stocked and therefore low": active and with a safety level. Whether it has no balance row anywhere is checked by the query.</summary>
    public static readonly Expression<Func<Product, bool>> CanBeNeverStockedLow = p => p.IsActive && p.SafetyStock > 0;
}
