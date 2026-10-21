using Ims.Domain;
namespace Ims.Application.Features.Purchasing;

public class PurchaseOrderReceiving(IReadRepository<StockDocumentLine> lines, IReadRepository<StockDocument> documents) : IPurchaseOrderReceiving, IAppService
{
    public async Task<IReadOnlyDictionary<int, ReceivedQuantity>> ReceivedAsync(int orderKey, CancellationToken ct) =>
        (await lines.ListAsync(new PostedReceiptQuantitiesSpec(orderKey), ct))
            .GroupBy(q => q.LineNo)
            .ToDictionary(g => g.Key, g => new ReceivedQuantity(g.Sum(q => q.Qty), g.Sum(q => q.BaseQty)));

    public async Task<IReadOnlyList<PurchaseOrderReceiptDto>> ReceiptsAsync(int orderKey, CancellationToken ct) =>
        await documents.ListAsync(new ReceiptsOfOrderSpec(orderKey), ct);
}
