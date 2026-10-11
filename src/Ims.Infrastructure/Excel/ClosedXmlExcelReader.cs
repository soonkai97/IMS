using ClosedXML.Excel;
using Ims.Application;
using Ims.Domain;
namespace Ims.Infrastructure.Excel;

public class ClosedXmlExcelReader : IExcelReader
{
    private const int HeaderRow = 1;

    public ExcelSheet Read(Stream file, int maxRows)
    {
        EnsureReasonableUnpackedSize(file);
        XLWorkbook wb;
        try { wb = new XLWorkbook(file); }
        catch (Exception) { throw new ValidationException(Messages.NotAWorkbook); }

        using (wb)
        {
            var ws = wb.Worksheets.FirstOrDefault() ?? throw new ValidationException(Messages.WorkbookEmpty);
            var headers = new Dictionary<int, string>();
            foreach (var cell in ws.Row(HeaderRow).CellsUsed())
                headers.TryAdd(cell.Address.ColumnNumber, Normalize(cell.GetString()));

            var last = ws.LastRowUsed()?.RowNumber() ?? HeaderRow;
            var dataRows = last - HeaderRow;
            if (dataRows > maxRows) throw new ValidationException(Messages.TooManyRows(dataRows, maxRows));

            var rows = new List<ExcelRow>();
            for (var i = HeaderRow + 1; i <= last; i++)
            {
                var cells = headers.ToDictionary(kv => kv.Value, kv => ws.Cell(i, kv.Key).GetFormattedString().Trim());
                if (cells.Values.All(string.IsNullOrEmpty)) continue;
                var numbers = new Dictionary<string, string>();
                foreach (var (name, column) in headers.Select(kv => (kv.Value, kv.Key)))
                    if (NumberOf(ws.Cell(i, column)) is { } number) numbers[name] = number;
                rows.Add(new ExcelRow(i, cells, numbers));
            }
            return new ExcelSheet(headers.Values.ToList(), rows);
        }
    }

    /// <summary>The number a cell holds (typed or the result of a formula) as invariant text with no exponent, or null when the cell is not a number.</summary>
    private static string? NumberOf(IXLCell cell)
    {
        if (cell.IsEmpty()) return null;
        XLCellValue value;
        try { value = cell.Value; }
        catch (Exception) { return null; } // a formula the reader cannot evaluate: fall back to what the cell shows
        if (!value.IsNumber) return null;
        try { return ((decimal)value.GetNumber()).ToString(System.Globalization.CultureInfo.InvariantCulture); }
        catch (OverflowException) { return null; }
    }

    /// <summary>
    /// An .xlsx is a zip. The upload limit applies to the compressed bytes, so the declared unpacked size is checked from the zip
    /// directory before anything is loaded: a small file cannot expand to gigabytes and exhaust memory (zip bomb).
    /// </summary>
    private static void EnsureReasonableUnpackedSize(Stream file)
    {
        if (!file.CanSeek) throw new ValidationException(Messages.NotAWorkbook);
        try
        {
            using var zip = new System.IO.Compression.ZipArchive(file, System.IO.Compression.ZipArchiveMode.Read, leaveOpen: true);
            long total = 0;
            foreach (var entry in zip.Entries)
            {
                total += entry.Length;
                if (total > Limits.MaxImportUncompressedBytes) throw new ValidationException(Messages.WorkbookTooLarge);
            }
        }
        catch (Exception ex) when (ex is not ValidationException)
        {
            throw new ValidationException(Messages.NotAWorkbook);
        }
        finally
        {
            file.Position = 0;
        }
    }

    private static string Normalize(string header) => header.Trim().ToLowerInvariant().Replace(" ", "").Replace("_", "");
}
