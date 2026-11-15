namespace Ims.Domain;

/// <summary>
/// A list of selling prices: the DEFAULT list (at most one, for every customer) or the list of ONE customer (at most one per customer), or a list that is neither yet. The server looks a price up in the customer's
/// list first, then in the default list (plan D10, built in PL3.12); a price list never changes a saved order. Its items are replaced as a whole by a save, like the lines of an order. The database enforces
/// what a race could break: one default, one list per customer, one item per product and unit, one name.
/// <para>
/// The list is an aggregate root (DDD-13, docs/architecture/ddd-roadmap.md 2.4): it raises <see cref="PriceListCreated"/>, <see cref="PriceListUpdated"/>, <see cref="PriceListCustomerSet"/> and
/// <see cref="PriceListDeleted"/> once a change is valid and made, carrying what the audit rows print, and a refused change raises nothing. The edit of a list is two calls (<see cref="Update"/>, then
/// <see cref="ReplaceItems"/>) and one audit row, so it is one event, raised by the second. The prices the save changed are captured BEFORE a kept item is repriced in place: afterwards the old price is gone.
/// </para>
/// </summary>
public sealed class PriceList : AggregateRoot
{
    private PriceList() { }

    public string Name { get; private set; } = "";

    /// <summary>The customer this list is for; null for the default list and for a list not yet assigned.</summary>
    public int? CustomerId { get; private set; }

    /// <summary>True for the one list that prices every customer who has no list of their own.</summary>
    public bool IsDefault { get; private set; }

    private readonly List<PriceListItem> _items = [];

    /// <summary>True from <see cref="Create"/> until <see cref="ReplaceItems"/> completes the create (the list is not complete before its items are in). Not a column: a list that EF loads is never new.</summary>
    private bool _isNew;

    /// <summary>The header before <see cref="Update"/> changed it, kept until <see cref="ReplaceItems"/> completes the edit and raises <see cref="PriceListUpdated"/> with it. Not a column.</summary>
    private PriceListSummary? _summaryBeforeEdit;

    /// <summary>Read-only view: items change only through <see cref="ReplaceItems"/>.</summary>
    public IReadOnlyList<PriceListItem> Items => _items;

    public Partner? Customer { get; private set; }

    public static PriceList Create(string name, bool isDefault, Partner? customer)
    {
        var list = new PriceList { _isNew = true };
        list.ApplyHeader(name, isDefault, customer);
        return list;
    }

    /// <summary>The name, whether it is the default list, and its customer (null clears it). The default list is for everyone, so it has no customer. Raises nothing by itself: the edit is complete when <see cref="ReplaceItems"/> follows.</summary>
    public void Update(string name, bool isDefault, Partner? customer)
    {
        _summaryBeforeEdit ??= Summary();
        ApplyHeader(name, isDefault, customer);
    }

    /// <summary>Gives the list to a customer, or takes it off (null). The customer must be an active customer, not a supplier. Raises <see cref="PriceListCustomerSet"/> on every valid call, even when the customer stays the same.</summary>
    public void SetCustomer(Partner? customer)
    {
        var before = CustomerId;
        ApplyCustomer(customer);
        Raise(new PriceListCustomerSet(PublicId, before, CustomerId));
    }

    /// <summary>
    /// Makes the items of the list the ones given. A product is priced once in each unit. An item the list already has keeps its row (and is repriced when the price differs), so a save that
    /// changes one price does not rewrite the rest; items no longer given are deleted as orphans by the persistence layer. Raises <see cref="PriceListCreated"/> for a new list and
    /// <see cref="PriceListUpdated"/> for a saved one, with the prices that changed as they were before this call.
    /// </summary>
    public void ReplaceItems(IEnumerable<PriceListItem> items)
    {
        var given = items.ToList();
        if (given.Count > Limits.MaxPriceListItems) throw new DomainException(Messages.PriceListTooManyItems(Limits.MaxPriceListItems));
        var seen = new HashSet<(int, int)>();
        foreach (var item in given)
            if (!seen.Add((item.ProductId, item.UomId))) throw new DomainException(Messages.PriceListDuplicateItemAny);

        var before = _summaryBeforeEdit ?? Summary();
        var was = _items.Select(Priced).ToList();   // taken now: a kept item is repriced in place below, and its old price is gone after that
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

        var changes = Changes(was, _items.Select(Priced).ToList());
        var isNew = _isNew;
        _isNew = false;
        _summaryBeforeEdit = null;
        Raise(isNew ? new PriceListCreated(PublicId, Summary(), changes) : new PriceListUpdated(PublicId, before, Summary(), changes));
    }

    /// <summary>The root's side of a delete: raises <see cref="PriceListDeleted"/> with the header and every item as a removed price. The rows are removed by the repository (the persistence layer turns the delete of the list and of its items into soft deletes).</summary>
    public void Delete() => Raise(new PriceListDeleted(PublicId, Summary(), Changes(_items.Select(Priced).ToList(), [])));

    private void ApplyHeader(string name, bool isDefault, Partner? customer)
    {
        var trimmed = name?.Trim() ?? "";
        if (trimmed.Length == 0) throw new DomainException(Messages.PriceListNameRequired);
        if (trimmed.Length > Limits.ShortNameMaxLength) throw new DomainException(Messages.PriceListNameTooLong);
        Name = trimmed;
        IsDefault = isDefault;
        ApplyCustomer(customer);
    }

    private void ApplyCustomer(Partner? customer)
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

    /// <summary>What the audit row of a create, an update or a delete prints about the header.</summary>
    private PriceListSummary Summary() => new(Name, IsDefault, CustomerId, _items.Count);

    /// <summary>An item as it is now: its key and its price, copied, so it does not follow the item when the item is repriced.</summary>
    private static (int ProductId, int UomId, decimal Price) Priced(PriceListItem item) => (item.ProductId, item.UomId, item.ListPrice);

    /// <summary>The prices that went from <paramref name="was"/> to <paramref name="now"/>: those added (in the order of now), then those removed (in the order of was), then those repriced (in the order of now).</summary>
    private static List<PriceChange> Changes(IReadOnlyCollection<(int ProductId, int UomId, decimal Price)> was, IReadOnlyCollection<(int ProductId, int UomId, decimal Price)> now)
    {
        var before = was.ToDictionary(i => (i.ProductId, i.UomId), i => i.Price);
        var after = now.ToDictionary(i => (i.ProductId, i.UomId), i => i.Price);

        var changes = new List<PriceChange>();
        changes.AddRange(now.Where(i => !before.ContainsKey((i.ProductId, i.UomId))).Select(i => new PriceChange(PriceChangeKind.Added, i.ProductId, i.UomId, null, Money.Rounded(i.Price))));
        changes.AddRange(was.Where(i => !after.ContainsKey((i.ProductId, i.UomId))).Select(i => new PriceChange(PriceChangeKind.Removed, i.ProductId, i.UomId, Money.Rounded(i.Price), null)));
        changes.AddRange(now.Where(i => before.TryGetValue((i.ProductId, i.UomId), out var old) && old != i.Price)
            .Select(i => new PriceChange(PriceChangeKind.Repriced, i.ProductId, i.UomId, Money.Rounded(before[(i.ProductId, i.UomId)]), Money.Rounded(i.Price))));
        return changes;
    }
}
