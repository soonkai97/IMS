namespace Ims.Domain;

/// <summary>
/// One price a save of a price list added, removed or changed: the product and the unit by integer key, and the price before (<see cref="Was"/>, none for an added item) and after (<see cref="Now"/>, none for a
/// removed item). A fact, not an entity: the aggregate captures the "before" because <c>ReplaceItems</c> reprices a kept item in place, so what it was is gone once the save is made. The audit row names the
/// product and the unit in words and prints a price with two places, which is all a <see cref="Money"/> keeps (a list price is stored in whole cents).
/// </summary>
public sealed record PriceChange(PriceChangeKind Kind, int ProductId, int UomId, Money? Was, Money? Now);
