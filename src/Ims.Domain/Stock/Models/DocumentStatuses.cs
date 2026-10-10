namespace Ims.Domain;

public static class DocumentStatuses
{
    /// <summary>Not posted yet and still alive: the stock figures of such a document are estimates and "on hand now" still says something about it.</summary>
    public static bool IsOpen(DocumentStatus status) => status is DocumentStatus.Draft or DocumentStatus.Submitted or DocumentStatus.Approved;
}
