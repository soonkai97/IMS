namespace Ims.Application;

/// <summary>
/// The document changed since the caller opened it (a stale If-Match). Carries who changed it last and when, for the UX E4 sentence
/// "{name} changed this document at {time}"; the name is filled in by <see cref="StaleDocumentBehavior"/>.
/// </summary>
public class StaleDocumentException(string documentNo, int? changedById, DateTime? changedAt, string? changedByName = null)
    : ConflictException(Ims.Domain.Messages.DocumentChangedByAnother(documentNo))
{
    public string DocumentNo { get; } = documentNo;
    public int? ChangedById { get; } = changedById;
    public DateTime? ChangedAt { get; } = changedAt;
    public string? ChangedByName { get; } = changedByName;
}
