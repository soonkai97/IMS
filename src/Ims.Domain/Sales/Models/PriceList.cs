namespace Ims.Domain;

/// <summary>
/// A list of selling prices: the DEFAULT list (at most one, for every customer) or the list of ONE customer (at most one per customer), or a list that is neither yet. The server looks a price up in the customer's
/// list first, then in the default list (plan D10, built in PL3.12); a price list never changes a saved order. Its items are replaced as a whole by a save, like the lines of an order. The database enforces
/// what a race could break: one default, one list per customer, one item per product and unit, one name.
/// </summary>
public sealed class PriceList : PublicEntity
{
    private PriceList() { }

    public string Name { get; private set; } = "";

    /// <summary>The customer this list is for; null for the default list and for a list not yet assigned.</summary>
    public int? CustomerId { get; private set; }

    /// <summary>True for the one list that prices every customer who has no list of their own.</summary>
    public bool IsDefault { get; private set; }

    private readonly List<PriceListItem> _items = [];

    /// <summary>Read-only view: items change only through <see cref="ReplaceItems"/>.</summary>
    public IReadOnlyList<PriceListItem> Items => _items;

    public Partner? Customer { get; private set; }

    public static PriceList Create(string name, bool isDefault, Partner? customer)
    {
        var list = new PriceList();
        list.Update(name, isDefault, customer);
        return list;
    }

    /// <summary>The name, whether it is the default list, and its customer (null clears it). The default list is for everyone, so it has no customer.</summary>
    public void Update(string name, bool isDefault, Partner? customer)
    {
        var trimmed = name?.Trim() ?? "";
        if (trimmed.Length == 0) throw new DomainException(Messages.PriceListNameRequired);
        if (trimmed.Length > Limits.ShortNameMaxLength) throw new DomainException(Messages.PriceListNameTooLong);
        Name = trimmed;
        IsDefault = isDefault;
        SetCustomer(customer);
    }

    /// <summary>Gives the list to a customer, or takes it off (null). The customer must be an active customer, not a supplier.</summary>
    public void SetCustomer(Partner? customer)
    {
        if (customer is null)
        {
            CustomerId = null;
            Customer = null;
            return;
        }
        if (IsDefault) throw new DomainException(Messages.PriceListDefaultHasNoCustomer);
        if (customer.Id != CustomerId && !customer.IsActive) throw new DomainException(Messages.PartnerInactive); // a customer who has the list and was deactivated since does not make the list unsavable
        if (customer.Type != PartnerType.Customer) throw new DomainException(Messages.PriceListCustomerRequiredType);
        Customer = customer;
        CustomerId = customer.Id;
    }

    /// <summary>
    /// Makes the items of the list the ones given. A product is priced once in each unit. An item the list already has keeps its row (and is repriced when the price differs), so a save that
    /// changes one price does not rewrite the rest; items no longer given are deleted as orphans by the persistence layer.
    /// </summary>
    public void ReplaceItems(IEnumerable<PriceListItem> items)
    {
        var given = items.ToList();
        if (given.Count > Limits.MaxPriceListItems) throw new DomainException(Messages.PriceListTooManyItems(Limits.MaxPriceListItems));
        var seen = new HashSet<(int, int)>();
        foreach (var item in given)
            if (!seen.Add((item.ProductId, item.UomId))) throw new DomainException(Messages.PriceListDuplicateItemAny);

        var existing = _items.ToDictionary(i => (i.ProductId, i.UomId));
        var result = new List<PriceListItem>(given.Count);
        foreach (var item in given)
        {
            if (!existing.TryGetValue((item.ProductId, item.UomId), out var kept)) { result.Add(item); continue; }
            if (kept.ListPrice != item.ListPrice) kept.Reprice(item.ListPrice);
            result.Add(kept);
        }
        _items.Clear();
        _items.AddRange(result);
    }
}
