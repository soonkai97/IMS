using static Ims.Domain.Tests.TestData;

namespace Ims.Domain.Tests.Sales;

/// <summary>
/// DDD-13: the price list is an aggregate root. It raises one domain event for each change the audit trail records today and carries what the audit rows print (docs/architecture/ddd-roadmap.md 2.4); a refused
/// change raises nothing. The edit of a list is two calls (<c>Update</c>, then <c>ReplaceItems</c>) and one audit row, so it is one event, raised by the second. The aggregate captures the prices of its items
/// BEFORE <c>ReplaceItems</c> reprices a kept item in place: once the save is made the old price is gone, and the audit row says "changed from 3.00 to 3.50".
/// </summary>
public class PriceListEventsTests
{
    private static PriceListItem Item(int productId, decimal price, int? uom = null) =>
        PriceListItem.Create(NewProduct(id: productId, sku: $"SKU-{productId}", cartonFactor: 12m), uom, price);

    private static Money M(decimal value) => Money.Rounded(value);

    /// <summary>A list as the create handler builds it, with the events of its creation cleared so that a test sees only what it does next (the state of a list loaded for an update, except that it is not tracked).</summary>
    private static PriceList Saved(params (int Product, decimal Price)[] items)
    {
        var list = PriceList.Create("Retail", false, null);
        list.ReplaceItems(items.Select(i => Item(i.Product, i.Price)));
        list.ClearDomainEvents();
        return list;
    }

    private static T Only<T>(PriceList list) where T : IDomainEvent => Assert.IsType<T>(Assert.Single(list.DomainEvents));

    // ----- created -----
    [Fact]
    public void Creating_a_list_and_putting_its_items_in_raises_one_Created_with_the_header_the_count_and_every_price_as_added_in_the_order_given()
    {
        var list = PriceList.Create("  Wholesale ", true, null);
        Assert.Empty(list.DomainEvents);   // the list is not complete until its items are in: the audit row counts them

        list.ReplaceItems([Item(3, 3m), Item(1, 12.5m), Item(2, 30m, CartonId)]);

        var created = Only<PriceListCreated>(list);
        Assert.Equal(list.PublicId, created.ListId);
        Assert.Equal(new PriceListSummary("Wholesale", true, null, 3), created.Summary);
        Assert.Equal(
            [
                new PriceChange(PriceChangeKind.Added, 3, PcsId, null, M(3m)),
                new PriceChange(PriceChangeKind.Added, 1, PcsId, null, M(12.50m)),
                new PriceChange(PriceChangeKind.Added, 2, CartonId, null, M(30m))
            ],
            created.Changes);
    }

    [Fact]
    public void A_list_created_with_no_items_raises_Created_with_no_changes_and_the_customer_it_was_made_for()
    {
        var list = PriceList.Create("Beta", false, NewCustomer(id: 7));

        list.ReplaceItems([]);

        var created = Only<PriceListCreated>(list);
        Assert.Equal(new PriceListSummary("Beta", false, 7, 0), created.Summary);
        Assert.Empty(created.Changes);
    }

    [Fact]
    public void A_second_ReplaceItems_on_the_new_list_is_an_update_because_the_create_is_complete()
    {
        var list = PriceList.Create("x", false, null);
        list.ReplaceItems([Item(1, 1m)]);
        list.ClearDomainEvents();

        list.ReplaceItems([Item(1, 2m)]);

        Assert.IsType<PriceListUpdated>(Assert.Single(list.DomainEvents));
    }

    // ----- updated -----
    [Fact]
    public void Updating_the_header_alone_raises_nothing_the_event_comes_with_the_items()
    {
        var list = Saved((1, 1m));

        list.Update("New name", true, null);

        Assert.Empty(list.DomainEvents);
    }

    [Fact]
    public void An_edit_raises_one_Updated_with_the_header_as_it_was_before_the_edit_and_as_it_is_after_it()
    {
        var list = Saved((1, 1m), (2, 2m));

        list.Update("  New name ", true, null);
        list.ReplaceItems([Item(1, 1m), Item(2, 2m), Item(3, 3m)]);

        var updated = Only<PriceListUpdated>(list);
        Assert.Equal(list.PublicId, updated.ListId);
        Assert.Equal(new PriceListSummary("Retail", false, null, 2), updated.Before);
        Assert.Equal(new PriceListSummary("New name", true, null, 3), updated.After);
    }

    [Fact]
    public void The_customer_of_the_header_before_and_after_the_edit_is_in_the_event_and_the_edit_does_not_raise_CustomerSet()
    {
        var list = PriceList.Create("Beta", false, NewCustomer(id: 7));
        list.ReplaceItems([]);
        list.ClearDomainEvents();

        list.Update("Beta", false, NewCustomer(id: 8));
        list.ReplaceItems([]);

        var updated = Only<PriceListUpdated>(list);   // the only event: Update does not raise CustomerSet
        Assert.Equal((7, 8), (updated.Before.CustomerId, updated.After.CustomerId));
    }

    [Fact]
    public void The_prices_before_ReplaceItems_overwrote_them_are_in_the_event_added_first_then_removed_then_repriced()
    {
        var list = Saved((1, 1m), (2, 3.00m), (3, 4m), (4, 5.5m));
        var repriced = list.Items[1];

        list.ReplaceItems([Item(5, 9m), Item(1, 1m), Item(2, 3.50m), Item(6, 10.5m)]);

        Assert.Equal(3.50m, repriced.ListPrice);   // the kept item now holds the new price: the aggregate took the old one before it was overwritten
        var updated = Only<PriceListUpdated>(list);
        Assert.Equal(
            [
                new PriceChange(PriceChangeKind.Added, 5, PcsId, null, M(9m)),
                new PriceChange(PriceChangeKind.Added, 6, PcsId, null, M(10.5m)),
                new PriceChange(PriceChangeKind.Removed, 3, PcsId, M(4m), null),
                new PriceChange(PriceChangeKind.Removed, 4, PcsId, M(5.5m), null),
                new PriceChange(PriceChangeKind.Repriced, 2, PcsId, M(3.00m), M(3.50m))
            ],
            updated.Changes);
    }

    [Fact]
    public void A_product_priced_in_two_units_is_two_items_and_each_is_compared_with_its_own_unit()
    {
        var list = PriceList.Create("x", false, null);
        list.ReplaceItems([Item(1, 1m), Item(1, 12m, CartonId)]);
        list.ClearDomainEvents();

        list.ReplaceItems([Item(1, 1m), Item(1, 13m, CartonId)]);

        Assert.Equal([new PriceChange(PriceChangeKind.Repriced, 1, CartonId, M(12m), M(13m))], Only<PriceListUpdated>(list).Changes);
    }

    [Theory]
    [InlineData("3", "3.00")]
    [InlineData("3.5", "3.50")]
    public void A_price_sent_as_the_same_number_at_another_scale_is_not_a_change(string was, string now)
    {
        var list = Saved((1, decimal.Parse(was, System.Globalization.CultureInfo.InvariantCulture)));

        list.ReplaceItems([Item(1, decimal.Parse(now, System.Globalization.CultureInfo.InvariantCulture))]);

        Assert.Empty(Only<PriceListUpdated>(list).Changes);
    }

    [Fact]
    public void A_save_that_changes_no_price_raises_Updated_with_no_changes_and_the_same_header_before_and_after()
    {
        var list = Saved((1, 1m));

        list.ReplaceItems([Item(1, 1m)]);

        var updated = Only<PriceListUpdated>(list);
        Assert.Empty(updated.Changes);
        Assert.Equal(updated.Before, updated.After);
    }

    [Fact]
    public void Two_edits_one_after_the_other_each_start_from_the_header_the_last_one_left()
    {
        var list = Saved();
        list.Update("First", false, null);
        list.ReplaceItems([]);
        list.ClearDomainEvents();

        list.Update("Second", false, null);
        list.ReplaceItems([]);

        Assert.Equal("First", Only<PriceListUpdated>(list).Before.Name);
    }

    [Fact]
    public void A_refused_save_raises_nothing_and_changes_nothing()
    {
        var list = Saved((1, 1m));

        Assert.Throws<DomainException>(() => list.ReplaceItems([Item(2, 1m), Item(2, 2m)]));                                                     // the same product and unit twice
        Assert.Throws<DomainException>(() => list.ReplaceItems(Enumerable.Range(1, Limits.MaxPriceListItems + 1).Select(i => Item(i, 1m))));     // over the cap
        Assert.Throws<DomainException>(() => list.Update("   ", false, null));                                                                    // no name

        Assert.Empty(list.DomainEvents);
        Assert.Equal(["SKU-1"], list.Items.Select(i => $"SKU-{i.ProductId}"));
    }

    // ----- customer set -----
    [Fact]
    public void Giving_the_list_to_a_customer_raises_CustomerSet_with_the_internal_customer_before_and_after()
    {
        var list = Saved();

        list.SetCustomer(NewCustomer(id: 7));
        list.SetCustomer(NewCustomer(id: 8));
        list.SetCustomer(null);

        Assert.Equal(
            [new PriceListCustomerSet(list.PublicId, null, 7), new PriceListCustomerSet(list.PublicId, 7, 8), new PriceListCustomerSet(list.PublicId, 8, null)],
            list.DomainEvents.Cast<PriceListCustomerSet>());
    }

    [Fact]
    public void A_call_that_changes_nothing_still_raises_CustomerSet_because_the_audit_row_is_written_every_time()
    {
        var list = Saved();
        list.SetCustomer(NewCustomer(id: 7));
        list.ClearDomainEvents();

        list.SetCustomer(NewCustomer(id: 7));
        list.SetCustomer(null);
        list.SetCustomer(null);

        Assert.Equal(
            [new PriceListCustomerSet(list.PublicId, 7, 7), new PriceListCustomerSet(list.PublicId, 7, null), new PriceListCustomerSet(list.PublicId, null, null)],
            list.DomainEvents.Cast<PriceListCustomerSet>());
    }

    [Fact]
    public void A_refused_customer_raises_nothing()
    {
        var plain = Saved();
        var standard = PriceList.Create("Default", true, null);

        Assert.Throws<DomainException>(() => plain.SetCustomer(NewSupplier()));
        Assert.Throws<DomainException>(() => plain.SetCustomer(NewCustomer(active: false)));
        Assert.Throws<DomainException>(() => standard.SetCustomer(NewCustomer()));

        Assert.Empty(plain.DomainEvents);
        Assert.Empty(standard.DomainEvents);
    }

    // ----- deleted -----
    [Fact]
    public void Deleting_raises_Deleted_with_the_header_and_every_price_as_removed_in_the_order_of_the_list()
    {
        var list = PriceList.Create("Doomed", false, NewCustomer(id: 7));
        list.ReplaceItems([Item(2, 22m, CartonId), Item(1, 4.25m)]);
        list.ClearDomainEvents();

        list.Delete();

        var deleted = Only<PriceListDeleted>(list);
        Assert.Equal(list.PublicId, deleted.ListId);
        Assert.Equal(new PriceListSummary("Doomed", false, 7, 2), deleted.Summary);
        Assert.Equal([new PriceChange(PriceChangeKind.Removed, 2, CartonId, M(22m), null), new PriceChange(PriceChangeKind.Removed, 1, PcsId, M(4.25m), null)], deleted.Changes);
    }

    [Fact]
    public void Deleting_a_list_with_no_items_raises_Deleted_with_no_changes()
    {
        var list = Saved();

        list.Delete();

        Assert.Empty(Only<PriceListDeleted>(list).Changes);
    }

    // ----- the base type -----
    [Fact]
    public void The_events_can_be_cleared_and_each_carries_its_sequence_number()
    {
        var list = Saved((1, 1m));
        list.ReplaceItems([Item(1, 2m)]);
        list.SetCustomer(NewCustomer(id: 7));

        Assert.Equal(2, list.RaisedEvents.Count);
        Assert.True(list.RaisedEvents[0].Sequence < list.RaisedEvents[1].Sequence);

        list.ClearDomainEvents();
        Assert.Empty(list.DomainEvents);
        Assert.Empty(list.RaisedEvents);
    }
}
