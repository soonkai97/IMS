using ClosedXML.Excel;
using Ims.Application;
namespace Ims.Infrastructure.Excel;

public class ClosedXmlExcelWriter : IExcelWriter
{
    private const string DateTimeFormat = "yyyy-mm-dd hh:mm";
    private const int HeaderRow = 1;

    public byte[] Write(string sheetName, string[] headers, IEnumerable<object?[]> rows)
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
                    case decimal d: cell.Value = d; break;
                    case DateTime dt: cell.Value = dt; cell.Style.DateFormat.Format = DateTimeFormat; break;
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
