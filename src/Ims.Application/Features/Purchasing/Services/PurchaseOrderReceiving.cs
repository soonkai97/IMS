using Ims.Domain;
namespace Ims.Application.Features.Purchasing;

public class PurchaseOrderReceiving(IReceivedQuantityReader quantities, IReadRepository<StockDocument> documents) : IPurchaseOrderReceiving, IAppService
{
    public Task<IReadOnlyDictionary<int, ReceivedQuantity>> ReceivedAsync(int orderKey, CancellationToken ct) => quantities.PostedByLineAsync(orderKey, ct);

    public async Task<IReadOnlyList<PurchaseOrderReceiptDto>> ReceiptsAsync(int orderKey, CancellationToken ct) =>
        await documents.ListAsync(new ReceiptsOfOrderSpec(orderKey), ct);
}
