using static Ims.Domain.Tests.TestData;

namespace Ims.Domain.Tests.Sales;

public class PriceListTests
{
    private static PriceListItem Item(int productId, decimal price, int? uom = null) =>
        PriceListItem.Create(NewProduct(id: productId, sku: $"SKU-{productId}", cartonFactor: 12m), uom, price);

    // ----- the list -----
    [Fact]
    public void A_list_has_a_trimmed_name_and_is_neither_default_nor_for_a_customer_until_told()
    {
        var list = PriceList.Create("  Retail  ", false, null);

        Assert.Equal(("Retail", false, null), (list.Name, list.IsDefault, list.CustomerId));
        Assert.Empty(list.Items);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_list_without_a_name_is_refused(string name)
    {
        Assert.Equal(Messages.PriceListNameRequired, Assert.Throws<DomainException>(() => PriceList.Create(name, false, null)).Message);
    }

    [Fact]
    public void A_name_of_200_characters_is_kept_and_one_more_is_refused()
    {
        Assert.Equal(200, PriceList.Create(new string('x', 200), false, null).Name.Length);
        Assert.Equal(Messages.PriceListNameTooLong, Assert.Throws<DomainException>(() => PriceList.Create(new string('x', 201), false, null)).Message);
    }

    [Fact]
    public void A_customer_list_belongs_to_an_active_customer_and_is_taken_off_with_null()
    {
        var customer = NewCustomer(id: 7);

        var list = PriceList.Create("Beta", false, customer);
        Assert.Equal(7, list.CustomerId);

        list.SetCustomer(null);
        Assert.Null(list.CustomerId);
    }

    [Fact]
    public void A_supplier_and_an_inactive_customer_cannot_have_a_list()
    {
        Assert.Equal(Messages.PriceListCustomerRequiredType, Assert.Throws<DomainException>(() => PriceList.Create("x", false, NewSupplier())).Message);
        Assert.Equal(Messages.PartnerInactive, Assert.Throws<DomainException>(() => PriceList.Create("x", false, NewCustomer(active: false))).Message);
    }

    [Fact]
    public void The_default_list_is_for_everybody_so_it_has_no_customer()
    {
        Assert.Equal(Messages.PriceListDefaultHasNoCustomer, Assert.Throws<DomainException>(() => PriceList.Create("x", true, NewCustomer())).Message);
        var list = PriceList.Create("x", true, null);

        Assert.Equal(Messages.PriceListDefaultHasNoCustomer, Assert.Throws<DomainException>(() => list.SetCustomer(NewCustomer())).Message);
        Assert.Equal(Messages.PriceListDefaultHasNoCustomer, Assert.Throws<DomainException>(() => PriceList.Create("y", false, NewCustomer()).Update("y", true, NewCustomer())).Message);
    }

    // ----- the items -----
    [Fact]
    public void An_item_keeps_its_product_its_unit_and_its_price_and_no_unit_means_the_base_unit()
    {
        var item = Item(5, 2.5m);
        var carton = Item(5, 30m, CartonId);

        Assert.Equal((5, PcsId, 2.5m), (item.ProductId, item.UomId, item.ListPrice));
        Assert.Equal((CartonId, 30m), (carton.UomId, carton.ListPrice)); // a price per carton is a price per carton
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    public void A_price_of_zero_or_less_is_refused(string price)
    {
        Assert.Equal(Messages.PriceListItemPricePositive, Assert.Throws<DomainException>(() => Item(1, decimal.Parse(price))).Message);
    }

    [Fact]
    public void A_price_has_at_most_two_decimals_and_at_most_the_limit()
    {
        Assert.Equal(Messages.PriceTooPrecise, Assert.Throws<DomainException>(() => Item(1, 1.005m)).Message);
        Assert.Equal(Messages.PriceTooLarge, Assert.Throws<DomainException>(() => Item(1, Limits.MaxUnitCost + 0.01m)).Message);
        Assert.Equal(Limits.MaxUnitCost, Item(1, Limits.MaxUnitCost).ListPrice);
        Assert.Equal(0.01m, Item(1, 0.01m).ListPrice);
    }

    [Fact]
    public void A_product_not_in_use_and_a_unit_it_cannot_convert_are_refused()
    {
        Assert.Throws<DomainException>(() => PriceListItem.Create(NewProduct(active: false), null, 1m));
        Assert.Throws<DomainException>(() => PriceListItem.Create(NewProduct(), 999, 1m));
    }

    [Fact]
    public void A_product_is_priced_once_in_each_unit()
    {
        var list = PriceList.Create("x", false, null);

        list.ReplaceItems([Item(1, 1m), Item(1, 12m, CartonId), Item(2, 1m)]);
        Assert.Equal(3, list.Items.Count);

        Assert.Equal(Messages.PriceListDuplicateItemAny, Assert.Throws<DomainException>(() => list.ReplaceItems([Item(1, 1m), Item(1, 2m)])).Message);
        Assert.Equal(3, list.Items.Count); // a refused save changes nothing
    }

    [Fact]
    public void A_list_holds_at_most_the_limit_of_items()
    {
        var list = PriceList.Create("x", false, null);
        var items = Enumerable.Range(1, Limits.MaxPriceListItems + 1).Select(i => Item(i, 1m)).ToList();

        Assert.Equal(Messages.PriceListTooManyItems(Limits.MaxPriceListItems), Assert.Throws<DomainException>(() => list.ReplaceItems(items)).Message);
        list.ReplaceItems(items.Take(Limits.MaxPriceListItems));
        Assert.Equal(Limits.MaxPriceListItems, list.Items.Count);
    }

    [Fact]
    public void Replacing_the_items_keeps_the_instance_of_an_item_it_keeps_reprices_it_in_place_and_takes_the_rest_as_given()
    {
        var list = PriceList.Create("x", false, null);
        list.ReplaceItems([Item(1, 1m), Item(2, 2m), Item(3, 3m)]);
        var kept = list.Items[0];
        var repriced = list.Items[1];
        var removed = list.Items[2];

        var added = Item(4, 4m);
        list.ReplaceItems([Item(1, 1m), Item(2, 2.5m), added]);

        Assert.Same(kept, list.Items[0]);
        Assert.Same(repriced, list.Items[1]);
        Assert.Equal(2.5m, repriced.ListPrice);
        Assert.Same(added, list.Items[2]);
        Assert.DoesNotContain(removed, list.Items);
    }

    // ----- what was fine when it was saved stays savable (PL3.12) -----
    [Fact]
    public void A_customer_who_has_the_list_and_was_deactivated_since_does_not_make_the_list_unsavable_but_a_new_inactive_customer_is_refused()
    {
        var customer = NewCustomer(id: 7);
        var list = PriceList.Create("Beta", false, customer);
        var deactivated = NewCustomer(id: 7, active: false);

        list.Update("Beta renamed", false, deactivated);   // the same customer, now inactive
        Assert.Equal((7, "Beta renamed"), (list.CustomerId, list.Name));

        Assert.Equal(Messages.PartnerInactive, Assert.Throws<DomainException>(() => list.SetCustomer(NewCustomer(id: 8, active: false))).Message);
        Assert.Equal(7, list.CustomerId);
    }

    [Fact]
    public void An_item_the_list_already_has_can_be_built_again_for_a_product_that_is_not_in_use_but_a_new_one_cannot()
    {
        var inactive = NewProduct(active: false);

        Assert.Throws<DomainException>(() => PriceListItem.Create(inactive, null, 1m));
        Assert.Equal(1m, PriceListItem.Create(inactive, null, 1m, requireUsable: false).ListPrice);
        Assert.Throws<DomainException>(() => PriceListItem.Create(inactive, null, 0m, requireUsable: false)); // every other rule still holds
    }
}
