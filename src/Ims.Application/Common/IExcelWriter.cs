namespace Ims.Application;

public interface IExcelWriter
{
    /// <param name="decimalPlaces">One entry per header: null for a column that is not money; 2 for money (shown as <c>#,##0.00</c>); 4 for a unit price (<c>#,##0.0000</c>). The numbers are rounded to these places, half away from zero.</param>
    byte[] Write(string sheetName, string[] headers, IReadOnlyList<int?> decimalPlaces, IEnumerable<object?[]> rows);
}
