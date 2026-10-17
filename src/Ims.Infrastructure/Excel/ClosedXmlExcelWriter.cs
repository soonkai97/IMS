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

    public byte[] Write(string sheetName, string[] headers, IReadOnlyList<int?> decimalPlaces, IEnumerable<object?[]> rows)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add(sheetName);
        for (var c = 0; c < headers.Length; c++) ws.Cell(HeaderRow, c + 1).Value = headers[c];
        ws.Row(HeaderRow).Style.Font.Bold = true;

        var r = HeaderRow + 1;
        foreach (var row in rows)
        {
            for (var c = 0; c < row.Length; c++)
            {
                var cell = ws.Cell(r, c + 1);
                switch (row[c])
                {
                    case null: break;
                    // A money cell is rounded once more here, so no sub-cent number can reach a file whatever produced the row.
                    case decimal d when decimalPlaces[c] is { } places: cell.Value = Money.Round(d, places); cell.Style.NumberFormat.Format = "#,##0." + new string('0', places); break;
                    case decimal d: cell.Value = d; break;
                    case DateTime dt: cell.Value = dt; cell.Style.DateFormat.Format = DateTimeFormat; break;
                    case DateOnly d: cell.Value = d.ToDateTime(TimeOnly.MinValue); cell.Style.DateFormat.Format = DateFormat; break;
                    case int i: cell.Value = i; break;
                    // Stored as an explicit text cell, so user-entered "=..." is never evaluated as a formula.
                    default: cell.SetValue(row[c]!.ToString()!); break;
                }
            }
            r++;
        }
        ws.SheetView.FreezeRows(HeaderRow);
        ws.Columns().AdjustToContents();

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }
}
