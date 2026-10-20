using ClosedXML.Excel;
using Ims.Application;
using Ims.Domain;
namespace Ims.Infrastructure.Excel;

public class ClosedXmlExcelWriter : IExcelWriter
{
    // dd/MM/yyyy everywhere (owner decision, same as the web). The cells stay real Excel dates, so sort and filter keep working.
    private const string DateFormat = "dd/mm/yyyy";
    private const string DateTimeFormat = "dd/mm/yyyy hh:mm";
    private const int HeaderRow = 1;

    // Column widths are fitted to the first rows only: fitting every cell of a 50,000 row sheet took 9 seconds of a 27 second export, and a column fitted to its first 2,000 rows is as
    // wide as the reader needs. A sheet of up to this many rows is fitted exactly as before.
    private const int AutoFitRows = 2_000;

    // Past those rows only the length of each value is noted, and a column is widened to the longest of them (so a large amount in row 30,000 is not shown as ####). Excel's widest column is 255.
    private const int MaxColumnWidth = 80;
    private const double WidthPadding = 2;

    public byte[] Write(string sheetName, string[] headers, IReadOnlyList<int?> decimalPlaces, IEnumerable<object?[]> rows)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add(sheetName);
        for (var c = 0; c < headers.Length; c++) ws.Cell(HeaderRow, c + 1).Value = headers[c];
        ws.Row(HeaderRow).Style.Font.Bold = true;

        // A number format is set once per column after the rows are in, not once per cell (per cell it took 10 seconds at 50,000 rows). The first format a column asks for becomes the
        // column's; a cell that asks for another one (a column that mixes kinds, not expected) gets its own, applied after the column's.
        var columnFormats = new Dictionary<int, string>();
        var ownFormats = new List<(IXLCell Cell, string Format)>();
        void Format(IXLCell cell, int column, string format)
        {
            if (columnFormats.TryAdd(column, format) || columnFormats[column] == format) return;
            ownFormats.Add((cell, format));
        }

        var widest = new double[headers.Length];
        var r = HeaderRow + 1;
        foreach (var row in rows)
        {
            for (var c = 0; c < row.Length; c++)
            {
                var cell = ws.Cell(r, c + 1);
                if (r > HeaderRow + AutoFitRows && c < widest.Length) widest[c] = Math.Max(widest[c], DisplayLength(row[c], c < decimalPlaces.Count ? decimalPlaces[c] : null));
                switch (row[c])
                {
                    case null: break;
                    // A money cell is rounded once more here, so no sub-cent number can reach a file whatever produced the row.
                    case decimal d when decimalPlaces[c] is { } places: cell.Value = Money.Round(d, places); Format(cell, c + 1, "#,##0." + new string('0', places)); break;
                    case decimal d: cell.Value = d; break;
                    case DateTime dt: cell.Value = dt; Format(cell, c + 1, DateTimeFormat); break;
                    case DateOnly d: cell.Value = d.ToDateTime(TimeOnly.MinValue); Format(cell, c + 1, DateFormat); break;
                    case int i: cell.Value = i; break;
                    // Stored as an explicit text cell, so user-entered "=..." is never evaluated as a formula.
                    default: cell.SetValue(row[c]!.ToString()!); break;
                }
            }
            r++;
        }
        foreach (var (column, format) in columnFormats) ws.Column(column).Style.NumberFormat.Format = format;
        foreach (var (cell, format) in ownFormats) cell.Style.NumberFormat.Format = format;
        ws.SheetView.FreezeRows(HeaderRow);
        ws.Columns().AdjustToContents(HeaderRow, HeaderRow + AutoFitRows);
        for (var c = 0; c < widest.Length; c++)
            if (widest[c] > 0 && ws.Column(c + 1).Width < widest[c] + WidthPadding) ws.Column(c + 1).Width = Math.Min(MaxColumnWidth, widest[c] + WidthPadding);

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    /// <summary>About how many characters a value takes in its cell: its text, or for money the digits with the thousands separators and the decimals.</summary>
    private static double DisplayLength(object? value, int? places) => value switch
    {
        null => 0,
        decimal d when places is { } p => Math.Abs(Math.Truncate(d)).ToString(System.Globalization.CultureInfo.InvariantCulture).Length * 4 / 3.0 + p + 2,
        decimal d => d.ToString(System.Globalization.CultureInfo.InvariantCulture).Length + 1,
        DateTime => 17,
        DateOnly => 11,
        _ => value.ToString()!.Length
    };
}
