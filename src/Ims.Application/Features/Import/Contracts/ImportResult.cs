namespace Ims.Application.Features.Import;

/// <param name="Created">Rows that create (a preview reports what a commit would create).</param>
/// <param name="Updated">Rows that update an existing record (a preview reports what a commit would update).</param>
public record ImportResult(bool Committed, int Total, int Valid, int Invalid, int Created, int Updated,
    int? DocumentId, IReadOnlyList<ImportRowResult> Rows);
