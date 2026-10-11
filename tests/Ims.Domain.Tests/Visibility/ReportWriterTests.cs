using Ims.Application;

namespace Ims.Domain.Tests.Visibility;

/// <summary>An Excel file leaves out the columns the caller may not see, header and cells alike.</summary>
public class ReportWriterTests
{
    private sealed class Recorder : IExcelWriter
    {
        public string[] Headers { get; private set; } = [];
        public IReadOnlyList<int?> DecimalPlaces { get; private set; } = [];
        public List<object?[]> Rows { get; } = [];

        public byte[] Write(string sheetName, string[] headers, IReadOnlyList<int?> decimalPlaces, IEnumerable<object?[]> rows)
        {
            Headers = headers;
            DecimalPlaces = decimalPlaces;
            Rows.AddRange(rows);
            return [];
        }
    }

    private sealed class Caller(params string[] permissions) : ICallerPermissions
    {
        public bool Has(string permission) => permissions.Contains(permission);
    }

    private static readonly ReportColumn[] Columns = [new("SKU"), new("Qty"), new("Unit cost", Permissions.CostView), new("Value", Permissions.ValuationView)];

    private static Recorder Write(params string[] permissions)
    {
        var excel = new Recorder();
        new ReportWriter(excel, new Caller(permissions)).Write("S", Columns, [["A", 2m, 3m, 6m], ["B", 1m, 4m, 4m]]);
        return excel;
    }

    [Fact]
    public void WithoutAnyPermission_BothCostColumnsAreGone()
    {
        var file = Write();

        Assert.Equal(["SKU", "Qty"], file.Headers);
        Assert.Equal([new object?[] { "A", 2m }, new object?[] { "B", 1m }], file.Rows);
    }

    [Fact]
    public void TheDecimalPlacesFollowTheColumnsThatAreKept_MoneyTwoAndAUnitPriceFour()
    {
        Assert.Equal(new int?[] { null, null }, Write().DecimalPlaces);
        Assert.Equal(new int?[] { null, null, 4 }, Write(Permissions.CostView).DecimalPlaces);   // "Unit cost" is a unit price
        Assert.Equal(new int?[] { null, null, 4, 2 }, Write(Permissions.CostView, Permissions.ValuationView).DecimalPlaces); // "Value" is money
    }

    [Fact]
    public void CostViewKeepsTheCostColumn_AndBothPermissionsKeepEverything()
    {
        Assert.Equal(["SKU", "Qty", "Unit cost"], Write(Permissions.CostView).Headers);
        Assert.Equal(["SKU", "Qty", "Unit cost", "Value"], Write(Permissions.CostView, Permissions.ValuationView).Headers);
        Assert.Equal(new object?[] { "B", 1m, 4m, 4m }, Write(Permissions.CostView, Permissions.ValuationView).Rows[1]);
    }
}
