using static Ims.Domain.Tests.TestData;

namespace Ims.Domain.Tests.Stock;

public class StockDocumentLineTests
{
    [Fact]
    public void Create_InBaseUnit_KeepsQuantityAndCost()
    {
        var product = NewProduct();

        var line = StockDocumentLine.Create(product, DocumentType.Receipt, 5m, null, 2.5m, "note");

        Assert.Equal(product.Id, line.ProductId);
        Assert.Equal(5m, line.Qty);
        Assert.Equal(PcsId, line.UomId);
        Assert.Equal(5m, line.BaseQty);
        Assert.Equal(2.5m, line.UnitCost);
        Assert.Equal("note", line.Remark);
    }

    [Fact]
    public void Create_ExplicitBaseUom_BehavesLikeNoUom()
    {
        var line = StockDocumentLine.Create(NewProduct(), DocumentType.Receipt, 5m, PcsId, 1m, null);

        Assert.Equal(PcsId, line.UomId);
        Assert.Equal(5m, line.BaseQty);
    }

    [Fact]
    public void Create_CartonToPiece_ConvertsQuantity()
    {
        var product = NewProduct(cartonFactor: 12m);

        var line = StockDocumentLine.Create(product, DocumentType.Receipt, 3m, CartonId, 120m, null);

        Assert.Equal(3m, line.Qty);
        Assert.Equal(CartonId, line.UomId);
        Assert.Equal(36m, line.BaseQty);
    }

    [Fact]
    public void Create_CostPerEnteredUnit_BecomesCostPerBaseUnit()
    {
        var product = NewProduct(cartonFactor: 12m);

        var line = StockDocumentLine.Create(product, DocumentType.Receipt, 3m, CartonId, 120m, null); // 120 per carton

        Assert.Equal(10m, line.UnitCost); // 10 per piece
        Assert.Equal(360m, line.BaseQty * line.UnitCost); // value is unchanged by the conversion: 3 cartons x 120
    }

    [Fact]
    public void Create_CostPerBaseUnitIsRoundedToConfiguredDecimals()
    {
        var product = NewProduct(cartonFactor: 3m);

        var line = StockDocumentLine.Create(product, DocumentType.Receipt, 1m, CartonId, 10m, null); // 10 / 3

        Assert.Equal(3.3333333333m, line.UnitCost);
    }

    [Fact]
    public void Create_FractionalFactor_ConvertsExactly()
    {
        var product = NewProduct(cartonFactor: 0.5m);

        var line = StockDocumentLine.Create(product, DocumentType.Receipt, 4m, CartonId, null, null);

        Assert.Equal(2m, line.BaseQty);
    }

    [Fact]
    public void Create_UnitWithoutConversion_Throws()
    {
        var product = NewProduct(); // no carton conversion

        var ex = Assert.Throws<DomainException>(() => StockDocumentLine.Create(product, DocumentType.Receipt, 1m, CartonId, 1m, null));

        Assert.Equal(Messages.ConversionMissing, ex.Message);
    }

    [Fact]
    public void Create_InactiveProduct_Throws()
    {
        var product = NewProduct(sku: "OLD-1", active: false);

        var ex = Assert.Throws<DomainException>(() => StockDocumentLine.Create(product, DocumentType.Receipt, 1m, null, 1m, null));

        Assert.Equal(Messages.ProductInactive("OLD-1"), ex.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-0.001)]
    public void Create_NonPositiveQuantity_Throws(double qty)
    {
        var ex = Assert.Throws<DomainException>(() => StockDocumentLine.Create(NewProduct(), DocumentType.Receipt, (decimal)qty, null, 1m, null));

        Assert.Equal(Messages.QtyPositive, ex.Message);
    }

    [Fact]
    public void Create_NegativeCost_Throws()
    {
        var ex = Assert.Throws<DomainException>(() => StockDocumentLine.Create(NewProduct(), DocumentType.Receipt, 1m, null, -0.01m, null));

        Assert.Equal(Messages.CostNegative, ex.Message);
    }

    [Fact]
    public void Create_ZeroCost_IsAllowed()
    {
        var line = StockDocumentLine.Create(NewProduct(), DocumentType.Receipt, 1m, null, 0m, null);

        Assert.Equal(0m, line.UnitCost);
    }

    [Fact]
    public void Create_ReceiptWithoutCost_StartsAtZero()
    {
        var line = StockDocumentLine.Create(NewProduct(), DocumentType.Receipt, 1m, null, null, null);

        Assert.Equal(0m, line.UnitCost);
    }

    [Fact]
    public void Create_IssueLine_StartsAtCostZeroEvenWhenACostIsEntered()
    {
        var line = StockDocumentLine.Create(NewProduct(), DocumentType.Issue, 2m, null, 99m, null);

        Assert.Equal(0m, line.UnitCost);
    }

    [Fact]
    public void BookIssueCost_SetsUnitCostToTheGivenAverage()
    {
        var line = StockDocumentLine.Create(NewProduct(), DocumentType.Issue, 2m, null, null, null);

        line.BookIssueCost(4.25m);

        Assert.Equal(4.25m, line.UnitCost);
    }
}
