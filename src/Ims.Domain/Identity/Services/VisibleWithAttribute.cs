namespace Ims.Domain;

/// <summary>
/// Marks a value that only a caller holding <see cref="Permission"/> may see (cost or value, see <see cref="Permissions.CostView"/> and
/// <see cref="Permissions.ValuationView"/>). This is the one place the rule is written; the pipeline reads it to set a tagged response property to
/// null (so it must be nullable), to remove the name from the audit log's JSON, to leave an export column out, and to refuse a tagged sort key with
/// 400. Architecture tests fail when a response type has a property that looks like cost or value and is not tagged.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
public sealed class VisibleWithAttribute(string permission) : Attribute
{
    public string Permission { get; } = permission;

    /// <summary>Name of a <c>bool</c> property of the same object: the value is withheld only while it is true (an approval trigger's numbers are money for an amount rule only).</summary>
    public string? OnlyWhen { get; init; }

    /// <summary>
    /// The tagged value is a derived cost per base unit (a unit price): what stays visible is rounded to <see cref="Money.UnitPriceDecimals"/> places instead of the 2
    /// of money, so 0.003 a piece is not shown as 0.00.
    /// </summary>
    public bool UnitPrice { get; init; }
}
