namespace Ims.Application.Features.Import;

public record ImportResult(bool Committed, int Total, int Valid, int Invalid, int Created, int Updated,
    int? DocumentId, IReadOnlyList<ImportRowResult> Rows);
