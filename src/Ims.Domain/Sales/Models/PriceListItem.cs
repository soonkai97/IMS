namespace Ims.Domain;

/// <summary>
/// One price of a price list: a product, the unit it is priced in, and the selling price per ONE of that unit. The unit is exact: a price per carton is a price per carton, never derived from a price per piece,
/// so no rounding between units happens (plan D10). A selling price is not cost: it is gated by <c>pricelist.manage</c>. The property has a name of its own (ListPrice) because the audit scrubber withholds a
/// tagged NAME unless the caller holds every permission that tags it, and the other price names are already tagged by other permissions.
/// </summary>
public class PriceListItem : Entity
{
    private PriceListItem() { }

    public int PriceListId { get; private set; }
    public int ProductId { get; private set; }
    public int UomId { get; private set; }

    /// <summary>Money per one <see cref="UomId"/>, at most 2 decimals, above 0.</summary>
    [VisibleWith(Permissions.PricelistManage)]
    public decimal ListPrice { get; private set; }

    public PriceList PriceList { get; private set; } = null!;
    public Product Product { get; private set; } = null!;
    public Uom Uom { get; private set; } = null!;

    /// <summary>Builds an item. The product must be usable (unless <paramref name="requireUsable"/> is false: an item the list already has, saved again unchanged, does not fail because its product was deactivated since) and the unit one the product converts to its base unit (null means the base unit); the price must be above 0, within the limit and in whole cents.</summary>
    public static PriceListItem Create(Product product, int? uomId, decimal price, bool requireUsable = true)
    {
        if (requireUsable) product.EnsureUsableOnDocuments();
        if (price <= 0) throw new DomainException(Messages.PriceListItemPricePositive);
        if (price > Limits.MaxUnitCost) throw new DomainException(Messages.PriceTooLarge);
        if (!Money.IsExact(price)) throw new DomainException(Messages.PriceTooPrecise);

        var uom = uomId ?? product.BaseUomId;
        product.ToBaseQuantity(1m, uom); // refuses a unit the product cannot convert

        // Only the keys are stored: the product may be a detached read copy, and assigning it as a navigation would make EF insert it again.
        return new PriceListItem { ProductId = product.Id, UomId = uom, ListPrice = price };
    }

    /// <summary>A save that keeps the item but prices it differently. The price was checked when the new item was built.</summary>
    internal void Reprice(decimal price) => ListPrice = price;
}
