using ClosedXML.Excel;
using Ims.Infrastructure.Excel;

namespace Ims.Api.Tests.Tests.Reports;

/// <summary>
/// Column widths are fitted to the first 2,000 rows of a sheet; a wider value further down must still fit, or Excel shows it as ####.
/// </summary>
public class ExcelWidthTests
{
    private static readonly string[] Headers = ["Name", "Quantity", "Amount"];
    private static readonly IReadOnlyList<int?> Places = [null, null, 2];

    private static double WidthOf(byte[] file, int column)
    {
        using var workbook = new XLWorkbook(new MemoryStream(file));
        return workbook.Worksheet(1).Column(column).Width;
    }

    private static byte[] Write(int rows, object?[] lastRow)
    {
        var all = Enumerable.Range(1, rows - 1).Select(i => new object?[] { "Item " + i, 5m, 12.5m }).Append(lastRow);
        return new ClosedXmlExcelWriter().Write("S", Headers, Places, all);
    }

    [Fact]
    public void A_large_amount_after_the_fitted_rows_still_has_room()
    {
        var file = Write(2_500, ["Item last", 123_456_789_012.34m, 9_876_543_210_987.65m]);

        // "9,876,543,210,987.65" is 20 characters.
        Assert.True(WidthOf(file, 3) >= 20, $"Amount column is {WidthOf(file, 3)} wide");
        Assert.True(WidthOf(file, 2) >= 15, $"Quantity column is {WidthOf(file, 2)} wide");
    }

    [Fact]
    public void A_long_text_after_the_fitted_rows_widens_its_column_but_never_past_the_cap()
    {
        var file = Write(2_500, [new string('x', 60), 5m, 12.5m]);
        var huge = Write(2_500, [new string('x', 500), 5m, 12.5m]);

        Assert.True(WidthOf(file, 1) >= 60);
        Assert.True(WidthOf(huge, 1) <= 80);
    }

    [Fact]
    public void A_small_sheet_is_fitted_to_its_content_as_before()
    {
        var file = Write(10, ["Item last", 5m, 12.5m]);

        Assert.True(WidthOf(file, 1) < 20);
    }
}
