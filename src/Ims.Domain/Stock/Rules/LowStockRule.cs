using System.Linq.Expressions;

namespace Ims.Domain;

/// <summary>
/// The one definition of "low stock", written as expressions so the database evaluates it and every screen uses the same one: the low-stock list and its
/// export, the Home count and the menu badge, the stock list's "low only" filter and the <c>isLow</c> flag of a stock row.
/// <para>
/// A product is low in an ACTIVE warehouse (a closed warehouse raises no alerts) when it is ACTIVE and either its on-hand quantity is below zero, or it has a safety level above zero and the on-hand
/// quantity is AT OR BELOW it (design: <c>docs/design/mvp-ux.md</c> 3.7 and 3.8, "on-hand &lt;= safety stock"; a receipt must bring the product ABOVE the level to take
/// it off the list). An active product with a safety level that has no balance row in ANY active warehouse the caller can see has never been stocked: it is listed
/// ONCE (quantity 0, no warehouse), not once per warehouse. Only the caller's own warehouses count (and an old row in an inactive one does not), so a scoped
/// caller learns nothing about stock elsewhere and the answer does not change when a warehouse they cannot see receives the product. A product that has a
/// balance row in a visible warehouse follows the rule above, per warehouse.
/// </para>
/// </summary>
public static class LowStockRule
{
    /// <summary>A balance row that is low: the first half of the definition.</summary>
    public static readonly Expression<Func<StockBalance, bool>> IsLowBalance =
        b => b.Warehouse.IsActive && b.Product.IsActive && (b.QtyOnHand < 0 || (b.Product.SafetyStock > 0 && b.QtyOnHand <= b.Product.SafetyStock));

    /// <summary>A product that can be "never stocked and therefore low": active and with a safety level. Whether it has no balance row in a visible warehouse is checked by the query.</summary>
    public static readonly Expression<Func<Product, bool>> CanBeNeverStockedLow = p => p.IsActive && p.SafetyStock > 0;

    /// <summary>
    /// Builds a projection that needs the verdict of <see cref="IsLowBalance"/> (the <c>isLow</c> flag of a stock row) without writing the condition a second
    /// time: <paramref name="shape"/> receives the balance and the verdict, and the verdict is replaced by the one rule.
    /// </summary>
    public static Expression<Func<StockBalance, TResult>> Project<TResult>(Expression<Func<StockBalance, bool, TResult>> shape)
    {
        var balance = shape.Parameters[0];
        var verdict = IsLowBalance.Body.ReplaceParameter(IsLowBalance.Parameters[0], balance);
        return Expression.Lambda<Func<StockBalance, TResult>>(shape.Body.ReplaceParameter(shape.Parameters[1], verdict), balance);
    }

    private static Expression ReplaceParameter(this Expression body, ParameterExpression parameter, Expression with) => new Replacer(parameter, with).Visit(body);

    private sealed class Replacer(ParameterExpression parameter, Expression with) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) => node == parameter ? with : base.VisitParameter(node);
    }
}
