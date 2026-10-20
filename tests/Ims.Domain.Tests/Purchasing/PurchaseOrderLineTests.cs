using static Ims.Domain.Tests.TestData;

namespace Ims.Domain.Tests.Purchasing;

public class PurchaseOrderLineTests
{
    private static PurchaseOrderLine Line(decimal qty, decimal price, int? uom = null, Product? product = null) =>
        PurchaseOrderLine.Create(product ?? NewProduct(cartonFactor: 12m), qty, uom, price, "note");

    [Fact]
    public void A_line_in_the_base_unit_keeps_quantity_and_price_and_values_itself()
    {
        var line = Line(5m, 2.5m);

        Assert.Equal((5m, 5m, 2.5m, 12.5m), (line.Qty, line.BaseQty, line.UnitPrice, line.Amount));
        Assert.Equal(PcsId, line.UomId);
        Assert.Equal("note", line.Remark);
    }

    [Fact]
    public void A_line_in_a_carton_converts_the_quantity_but_prices_the_entered_unit()
    {
        var line = Line(3m, 120m, CartonId);

        Assert.Equal(36m, line.BaseQty);
        Assert.Equal(360m, line.Amount); // 3 cartons x 120.00, no division by the 12 pieces in between
    }

    [Theory]
    [InlineData("0.5", "0.01", "0.01")]   // 0.005 rounds up to 0.01, half away from zero as on a receipt line
    [InlineData("1.5", "0.01", "0.02")]   // 0.015 rounds up to 0.02
    [InlineData("3", "0.33", "0.99")]
    [InlineData("0.0001", "0.01", "0.00")]
    public void The_amount_is_the_entered_quantity_times_the_entered_price_rounded_half_away_from_zero(string qty, string price, string expected)
    {
        Assert.Equal(decimal.Parse(expected), Line(decimal.Parse(qty), decimal.Parse(price)).Amount);
    }

    [Fact]
    public void A_price_of_zero_is_a_valid_draft_line()
    {
        Assert.Equal(0m, Line(4m, 0m).Amount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_quantity_of_zero_or_less_is_refused(int qty)
    {
        Assert.Equal(Messages.QtyPositive, Assert.Throws<DomainException>(() => Line(qty, 1m)).Message);
    }

    [Fact]
    public void A_negative_price_a_price_above_the_limit_and_a_fraction_of_a_cent_are_each_refused_with_their_own_sentence()
    {
        Assert.Equal(Messages.PriceNegative, Assert.Throws<DomainException>(() => Line(1m, -0.01m)).Message);
        Assert.Equal(Messages.PriceTooLarge, Assert.Throws<DomainException>(() => Line(1m, Limits.MaxUnitCost + 0.01m)).Message);
        Assert.Equal(Messages.PriceTooPrecise, Assert.Throws<DomainException>(() => Line(1m, 1.005m)).Message);
    }

    [Fact]
    public void An_amount_above_the_limit_is_refused_not_overflowed()
    {
        Assert.Equal(Messages.LineAmountTooLarge, Assert.Throws<DomainException>(() => Line(Limits.MaxQuantity, Limits.MaxUnitCost)).Message);
    }

    [Fact]
    public void A_quantity_that_passes_the_base_limit_in_a_big_unit_is_refused()
    {
        var product = NewProduct(cartonFactor: 1_000_000_000m);

        Assert.Equal(Messages.QtyTooLarge, Assert.Throws<DomainException>(() => Line(Limits.MaxQuantity, 0m, CartonId, product)).Message);
    }

    [Fact]
    public void An_inactive_product_cannot_be_ordered()
    {
        Assert.Throws<DomainException>(() => Line(1m, 1m, product: NewProduct(active: false)));
    }

    [Fact]
    public void A_unit_the_product_cannot_convert_is_refused()
    {
        Assert.Throws<DomainException>(() => Line(1m, 1m, uom: 99, product: NewProduct()));
    }
}
