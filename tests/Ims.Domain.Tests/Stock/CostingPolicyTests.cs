namespace Ims.Domain.Tests.Stock;

public class CostingPolicyTests
{
    [Theory]
    [InlineData(10.0, 1.0, 1.0, 10.0)]     // base unit entered: unchanged
    [InlineData(120.0, 3.0, 36.0, 10.0)]   // 3 cartons @ 120 = 36 pcs @ 10
    [InlineData(10.0, 1.0, 3.0, 3.3333333333)] // rounded to 10 decimals (Limits.CostDecimals)
    [InlineData(5.0, 2.0, 1.0, 10.0)]      // entered unit smaller than base: cost per base unit goes up
    [InlineData(0.0, 4.0, 8.0, 0.0)]
    public void BaseUnitCost_Receipt_ConvertsEnteredCostToCostPerBaseUnit(double cost, double enteredQty, double baseQty, double expected)
    {
        var result = CostingPolicy.BaseUnitCost(DocumentType.Receipt, (decimal)cost, (decimal)enteredQty, (decimal)baseQty);

        Assert.Equal((decimal)expected, result);
    }

    [Fact]
    public void BaseUnitCost_Receipt_PreservesTotalValue()
    {
        var perBase = CostingPolicy.BaseUnitCost(DocumentType.Receipt, 120m, 3m, 36m);

        Assert.Equal(3m * 120m, 36m * perBase);
    }

    [Fact]
    public void BaseUnitCost_ReceiptWithoutCost_IsZero()
    {
        Assert.Equal(0m, CostingPolicy.BaseUnitCost(DocumentType.Receipt, null, 3m, 36m));
    }

    [Theory]
    [InlineData(DocumentType.Issue)]
    [InlineData(DocumentType.Transfer)]
    [InlineData(DocumentType.Adjust)]
    public void BaseUnitCost_NonReceipt_IsZeroEvenWithACost(DocumentType type)
    {
        Assert.Equal(0m, CostingPolicy.BaseUnitCost(type, 50m, 1m, 1m));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void BaseUnitCost_NonPositiveBaseQuantity_IsZeroInsteadOfDividingByZero(int baseQty)
    {
        Assert.Equal(0m, CostingPolicy.BaseUnitCost(DocumentType.Receipt, 10m, 1m, baseQty));
    }
}
