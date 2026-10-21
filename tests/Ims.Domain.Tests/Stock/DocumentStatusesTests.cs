namespace Ims.Domain.Tests.Stock;

public class DocumentStatusesTests
{
    [Theory]
    [InlineData(DocumentStatus.Draft, true)]
    [InlineData(DocumentStatus.Submitted, true)]
    [InlineData(DocumentStatus.Approved, true)]
    [InlineData(DocumentStatus.Posted, true)]
    [InlineData(DocumentStatus.Cancelled, false)]
    [InlineData(DocumentStatus.Reversed, false)]
    public void A_receipt_blocks_the_cancel_of_its_purchase_order_while_it_is_open_or_posted_and_not_once_it_is_cancelled_or_reversed(DocumentStatus status, bool blocks)
    {
        Assert.Equal(blocks, DocumentStatuses.BlocksPurchaseOrderCancel(status));
    }
}
