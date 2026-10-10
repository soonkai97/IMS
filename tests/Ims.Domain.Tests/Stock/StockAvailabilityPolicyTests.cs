using static Ims.Domain.Tests.TestData;

namespace Ims.Domain.Tests.Stock;

public class StockAvailabilityPolicyTests
{
    private static Product P(int id) => NewProduct(id, $"SKU-{id}");

    [Fact]
    public void EnsureAvailable_EnoughOnHand_DoesNotThrow()
    {
        var doc = NewIssue(2, (P(1), 5m), (P(2), 3m));

        StockAvailabilityPolicy.EnsureAvailable(doc, new Dictionary<int, decimal> { [1] = 5m, [2] = 10m });
    }

    [Fact]
    public void EnsureAvailable_ExactlyOnHand_DoesNotThrow()
    {
        var doc = NewIssue(2, (P(1), 5m));

        StockAvailabilityPolicy.EnsureAvailable(doc, new Dictionary<int, decimal> { [1] = 5m });
    }

    [Fact]
    public void EnsureAvailable_ShortOfOneProduct_ThrowsWithThatProductsFigures()
    {
        var doc = NewIssue(2, (P(1), 5m), (P(2), 8m));

        var ex = Assert.Throws<InsufficientStockException>(() =>
            StockAvailabilityPolicy.EnsureAvailable(doc, new Dictionary<int, decimal> { [1] = 5m, [2] = 7.5m }));

        Assert.Equal(2, ex.ProductId);
        Assert.Equal(doc.WarehouseId, ex.WarehouseId);
        Assert.Equal(7.5m, ex.Available);
        Assert.Equal(8m, ex.Requested);
    }

    [Fact]
    public void EnsureAvailable_SumsLinesOfTheSameProduct()
    {
        // Each line alone is fine (6 <= 10) but together they ask for 12.
        var doc = NewIssue(2, (P(1), 6m), (P(1), 6m));

        var ex = Assert.Throws<InsufficientStockException>(() =>
            StockAvailabilityPolicy.EnsureAvailable(doc, new Dictionary<int, decimal> { [1] = 10m }));

        Assert.Equal(12m, ex.Requested);
        Assert.Equal(10m, ex.Available);
    }

    [Fact]
    public void EnsureAvailable_ProductMissingFromBalances_CountsAsZero()
    {
        var doc = NewIssue(2, (P(9), 1m));

        var ex = Assert.Throws<InsufficientStockException>(() =>
            StockAvailabilityPolicy.EnsureAvailable(doc, new Dictionary<int, decimal>()));

        Assert.Equal(0m, ex.Available);
    }

    [Fact]
    public void EnsureAvailable_NegativeOnHand_IsInsufficient()
    {
        var doc = NewIssue(2, (P(1), 1m));

        Assert.Throws<InsufficientStockException>(() =>
            StockAvailabilityPolicy.EnsureAvailable(doc, new Dictionary<int, decimal> { [1] = -3m }));
    }

    [Fact]
    public void EnsureAvailable_ReportsTheLowestProductIdFirst()
    {
        var doc = NewIssue(2, (P(7), 5m), (P(3), 5m));

        var ex = Assert.Throws<InsufficientStockException>(() =>
            StockAvailabilityPolicy.EnsureAvailable(doc, new Dictionary<int, decimal>()));

        Assert.Equal(3, ex.ProductId);
    }

    [Fact]
    public void EnsureAvailable_UsesBaseQuantity_NotTheEnteredQuantity()
    {
        var carton = NewProduct(1, cartonFactor: 12m);
        var doc = StockDocument.Create(DocumentType.Issue, NewWarehouse(), null, null, null, Creator, Now).WithId(2);
        doc.AddLines([NewLine(carton, DocumentType.Issue, 2m, uomId: CartonId)]); // 24 pieces

        Assert.Throws<InsufficientStockException>(() =>
            StockAvailabilityPolicy.EnsureAvailable(doc, new Dictionary<int, decimal> { [1] = 23m }));
        StockAvailabilityPolicy.EnsureAvailable(doc, new Dictionary<int, decimal> { [1] = 24m });
    }

    [Fact]
    public void IsShort_EnoughOnHand_IsFalse()
    {
        var doc = NewIssue(2, (P(1), 5m), (P(2), 3m));

        Assert.False(StockAvailabilityPolicy.IsShort(doc, new Dictionary<int, decimal> { [1] = 5m, [2] = 3m }));
    }

    [Fact]
    public void IsShort_OneProductShort_IsTrue()
    {
        var doc = NewIssue(2, (P(1), 5m), (P(2), 3m));

        Assert.True(StockAvailabilityPolicy.IsShort(doc, new Dictionary<int, decimal> { [1] = 5m, [2] = 2.9m }));
    }

    [Fact]
    public void IsShort_ProductWithoutBalanceRow_CountsAsZeroOnHand()
    {
        var doc = NewIssue(2, (P(1), 1m));

        Assert.True(StockAvailabilityPolicy.IsShort(doc, new Dictionary<int, decimal>()));
    }

    [Fact]
    public void IsShort_SumsLinesOfTheSameProduct()
    {
        var doc = NewIssue(2, (P(1), 3m), (P(1), 4m));

        Assert.True(StockAvailabilityPolicy.IsShort(doc, new Dictionary<int, decimal> { [1] = 6m }));
        Assert.False(StockAvailabilityPolicy.IsShort(doc, new Dictionary<int, decimal> { [1] = 7m }));
    }

    [Fact]
    public void EnsureAvailable_DocumentWithoutLines_DoesNotThrow()
    {
        var doc = StockDocument.Create(DocumentType.Issue, NewWarehouse(), null, null, null, Creator, Now);

        StockAvailabilityPolicy.EnsureAvailable(doc, new Dictionary<int, decimal>());
    }
}
