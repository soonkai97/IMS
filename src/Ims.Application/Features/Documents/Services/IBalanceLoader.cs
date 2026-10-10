using Ims.Domain;
namespace Ims.Application.Features.Documents;

public interface IBalanceLoader
{
    /// <summary>Tracked balances for every product on the document; missing ones are created (and staged) with zero stock.</summary>
    Task<Dictionary<int, StockBalance>> LoadForUpdateAsync(StockDocument document, CancellationToken ct);
}
