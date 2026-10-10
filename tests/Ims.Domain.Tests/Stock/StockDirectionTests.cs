using Ims.Domain;

namespace Ims.Domain.Tests.Stock;

public class StockDirectionTests
{
    [Theory]
    [InlineData(DocumentType.Receipt, 5, 5)]
    [InlineData(DocumentType.Issue, 5, -5)]
    public void A_posting_adds_stock_for_a_receipt_and_removes_it_for_an_issue(DocumentType type, double qty, double expected) =>
        Assert.Equal((decimal)expected, StockDirection.EffectOf(type, (decimal)qty));

    [Theory]
    [InlineData(DocumentType.Receipt, 5, -5)]
    [InlineData(DocumentType.Issue, 5, 5)]
    public void A_reversal_does_the_opposite_of_the_posting(DocumentType type, double qty, double expected) =>
        Assert.Equal((decimal)expected, StockDirection.EffectOf(type, (decimal)qty, reversal: true));

    [Theory]
    [InlineData(DocumentType.Receipt)]
    [InlineData(DocumentType.Issue)]
    public void A_posting_and_its_reversal_cancel_out(DocumentType type) =>
        Assert.Equal(0m, StockDirection.EffectOf(type, 7.5m) + StockDirection.EffectOf(type, 7.5m, reversal: true));

    [Theory]
    [InlineData(DocumentType.Transfer)]
    [InlineData(DocumentType.Adjust)]
    public void Types_without_a_posting_strategy_have_no_direction_yet(DocumentType type)
    {
        var ex = Assert.Throws<DomainException>(() => StockDirection.Of(type));

        Assert.Equal(Messages.PostingNotSupported(type), ex.Message);
    }
}
