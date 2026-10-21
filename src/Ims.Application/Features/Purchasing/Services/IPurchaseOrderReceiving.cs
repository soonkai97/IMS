using Ims.Domain;
namespace Ims.Application.Features.Purchasing;

/// <summary>What has been received against an order, computed from the receipts on every call (plan D4: never stored, so nothing can drift and a reversal needs no code).</summary>
public interface IPurchaseOrderReceiving
{
    /// <summary>Per order line number: the quantity of the Posted receipts linked to the order. A line with nothing posted is absent.</summary>
    Task<IReadOnlyDictionary<int, ReceivedQuantity>> ReceivedAsync(int orderKey, CancellationToken ct);

    /// <summary>The receipts created from the order, in every state, oldest first.</summary>
    Task<IReadOnlyList<PurchaseOrderReceiptDto>> ReceiptsAsync(int orderKey, CancellationToken ct);
}
