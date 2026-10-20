namespace Ims.Application;

/// <summary>The seek-paged lists. Each is a purpose of its own for <see cref="ICursorProtector"/>: a cursor of the ledger is refused by the documents list.</summary>
public static class CursorLists
{
    public const string Documents = "documents";
    public const string PurchaseOrders = "purchase-orders";
    public const string Ledger = "ledger";
    public const string Audit = "audit";
}
