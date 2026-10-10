namespace Ims.Api.Tests.Support;

/// <summary>The first worksheet of an xlsx file as plain text, for asserting on downloaded files.</summary>
public sealed record XlsxContent(string SheetName, IReadOnlyList<string> Headers, IReadOnlyList<IReadOnlyList<string>> Rows)
{
    /// <summary>The first data row whose cell in <paramref name="column"/> equals <paramref name="value"/>, or null.</summary>
    public IReadOnlyList<string>? FindRow(string column, string value)
    {
        var index = Headers.ToList().IndexOf(column);
        return index < 0 ? null : Rows.FirstOrDefault(r => r[index] == value);
    }

    public string Cell(IReadOnlyList<string> row, string column) => row[Headers.ToList().IndexOf(column)];
}
