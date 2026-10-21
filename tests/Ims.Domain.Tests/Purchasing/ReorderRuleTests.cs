namespace Ims.Domain.Tests.Purchasing;

/// <summary>PL2.6: the suggested quantity (plan D8): twice the safety level less on hand less on order, never below zero. Whole figures in the plan's own examples, then the edges.</summary>
public class ReorderRuleTests
{
    [Theory]
    [InlineData("10", "3", "0", "17")]     // plan acceptance 1: 2 x 10 - 3 - 0
    [InlineData("10", "3", "10", "7")]     // an Approved line of 10 remaining
    [InlineData("10", "3", "17", "0")]     // 17 on order covers it: the row is not listed
    [InlineData("10", "3", "40", "0")]     // on order far above the need never goes negative
    [InlineData("10", "10", "0", "10")]    // exactly on the safety level still suggests the other half
    [InlineData("10", "0", "0", "20")]     // nothing on hand
    [InlineData("0", "-4", "0", "4")]      // negative on hand, no safety level: bring it back to zero
    [InlineData("0", "-4", "3", "1")]
    [InlineData("10", "-4", "0", "24")]    // negative counts as it is, not as zero
    [InlineData("0", "0", "0", "0")]       // nothing to order
    [InlineData("2.5", "1.2", "0.3", "3.5")] // fractions keep the scale of every quantity
    public void The_suggestion_is_twice_the_safety_level_less_on_hand_and_on_order_and_never_below_zero(string safety, string onHand, string onOrder, string expected)
    {
        Assert.Equal(decimal.Parse(expected), ReorderRule.SuggestedQty(decimal.Parse(safety), decimal.Parse(onHand), decimal.Parse(onOrder)));
    }

    [Fact]
    public void Only_the_open_orders_count_as_on_order_not_a_closed_or_cancelled_one()
    {
        Assert.Equal([PurchaseOrderStatus.Draft, PurchaseOrderStatus.Submitted, PurchaseOrderStatus.Approved], ReorderRule.OnOrderStatuses);
        Assert.DoesNotContain(PurchaseOrderStatus.Closed, ReorderRule.OnOrderStatuses);
        Assert.DoesNotContain(PurchaseOrderStatus.Cancelled, ReorderRule.OnOrderStatuses);
    }
}
