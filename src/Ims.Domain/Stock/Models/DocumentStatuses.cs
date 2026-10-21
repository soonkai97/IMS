namespace Ims.Domain;

public static class DocumentStatuses
{
    /// <summary>Not posted yet and still alive: the stock figures of such a document are estimates and "on hand now" still says something about it.</summary>
    public static bool IsOpen(DocumentStatus status) => status is DocumentStatus.Draft or DocumentStatus.Submitted or DocumentStatus.Approved;

    /// <summary>A receipt in this state keeps its purchase order from being cancelled: it is still to be decided (open) or it brought stock in (Posted). A Cancelled or Reversed one is history and does not.</summary>
    public static bool BlocksPurchaseOrderCancel(DocumentStatus status) => IsOpen(status) || status == DocumentStatus.Posted;
}
