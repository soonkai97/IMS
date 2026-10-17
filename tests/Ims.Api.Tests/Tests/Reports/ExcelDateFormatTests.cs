using ClosedXML.Excel;
using Ims.Infrastructure.Excel;

namespace Ims.Api.Tests.Tests.Reports;

/// <summary>Dates in an Excel export read dd/MM/yyyy, and stay real dates so Excel can still sort and filter them.</summary>
public class ExcelDateFormatTests
{
    private static IXLWorksheet Write(params object?[] row)
    {
        var bytes = new ClosedXmlExcelWriter().Write("S", row.Select((_, i) => $"C{i}").ToArray(), row.Select(_ => (int?)null).ToList(), [row]);
        return new XLWorkbook(new MemoryStream(bytes)).Worksheet(1);
    }

    [Fact]
    public void A_date_time_cell_is_a_real_date_time_shown_as_dd_MM_yyyy_24h()
    {
        var cell = Write(new DateTime(2026, 10, 7, 14, 5, 0)).Cell(2, 1);

        Assert.Equal(XLDataType.DateTime, cell.DataType);
        Assert.Equal(new DateTime(2026, 10, 7, 14, 5, 0), cell.GetDateTime());
        Assert.Equal("dd/mm/yyyy hh:mm", cell.Style.DateFormat.Format);
        Assert.Equal("07/10/2026 14:05", cell.GetFormattedString());
    }

    [Fact]
    public void A_date_only_cell_is_a_real_date_shown_as_dd_MM_yyyy()
    {
        var cell = Write(new DateOnly(2026, 10, 7)).Cell(2, 1);

        Assert.Equal(XLDataType.DateTime, cell.DataType);
        Assert.Equal(new DateTime(2026, 10, 7), cell.GetDateTime());
        Assert.Equal("dd/mm/yyyy", cell.Style.DateFormat.Format);
        Assert.Equal("07/10/2026", cell.GetFormattedString());
    }
}
