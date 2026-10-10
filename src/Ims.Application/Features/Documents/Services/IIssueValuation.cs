using Ims.Domain;
namespace Ims.Application.Features.Documents;

public interface IIssueValuation
{
    /// <summary>
    /// Values an unposted issue at the current average cost of its products (one balance query for the whole document), so lists, the document
    /// page and the approval rules all see the same amount. Receipts and posted documents are left alone.
    /// </summary>
    Task EstimateAsync(StockDocument document, CancellationToken ct);
}
