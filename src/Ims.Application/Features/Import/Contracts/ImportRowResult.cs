namespace Ims.Application.Features.Import;

public record ImportRowResult(int Row, string Key, bool Ok, IReadOnlyList<string> Errors);
