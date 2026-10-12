namespace Ims.Application.Features.Import;

/// <param name="Created">Rows that create (a preview reports what a commit would create).</param>
/// <param name="Updated">Rows that update an existing record (a preview reports what a commit would update).</param>
/// <param name="DocumentId">The public id of the first draft receipt a committed opening-stock import created; null for a preview and for other imports.</param>
public record ImportResult(bool Committed, int Total, int Valid, int Invalid, int Created, int Updated,
    Guid? DocumentId, IReadOnlyList<ImportRowResult> Rows);
