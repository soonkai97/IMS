namespace Ims.Application;

/// <summary>Header names are normalised to lower case without spaces or underscores.</summary>
public sealed record ExcelSheet(IReadOnlyList<string> Headers, IReadOnlyList<ExcelRow> Rows);
