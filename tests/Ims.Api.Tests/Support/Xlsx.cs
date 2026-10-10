using ClosedXML.Excel;

namespace Ims.Api.Tests.Support;

/// <summary>Builds and reads small xlsx files in memory.</summary>
public static class Xlsx
{
    public const string ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public static byte[] Create(string sheetName, string[] headers, params object?[][] rows)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add(sheetName);
        for (var c = 0; c < headers.Length; c++) sheet.Cell(1, c + 1).Value = headers[c];
        for (var r = 0; r < rows.Length; r++)
            for (var c = 0; c < rows[r].Length; c++)
                if (rows[r][c] is { } value) sheet.Cell(r + 2, c + 1).Value = XLCellValue.FromObject(value);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    public static XlsxContent Read(byte[] bytes)
    {
        using var workbook = new XLWorkbook(new MemoryStream(bytes));
        var sheet = workbook.Worksheets.First();
        var lastColumn = sheet.Row(1).LastCellUsed()?.Address.ColumnNumber ?? 0;
        var lastRow = sheet.LastRowUsed()?.RowNumber() ?? 1;

        var headers = Enumerable.Range(1, lastColumn).Select(c => sheet.Cell(1, c).GetFormattedString()).ToList();
        var rows = Enumerable.Range(2, Math.Max(0, lastRow - 1))
            .Select(r => (IReadOnlyList<string>)Enumerable.Range(1, lastColumn).Select(c => sheet.Cell(r, c).GetFormattedString()).ToList())
            .ToList();
        return new XlsxContent(sheet.Name, headers, rows);
    }
}
