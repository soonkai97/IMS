namespace Ims.Application.Features.Import;

/// <param name="Ok">False when the row has errors and blocks the import.</param>
/// <param name="Status">New or Update for a valid row (what a commit would do with it; the same in a preview and after a commit), Error otherwise. A valid row can still carry a note in <c>Errors</c> (for example "unit not changed").</param>
public record ImportRowResult(int Row, string Key, bool Ok, ImportRowStatus Status, IReadOnlyList<string> Errors);
