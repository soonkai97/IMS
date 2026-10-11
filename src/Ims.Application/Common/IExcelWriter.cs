namespace Ims.Application;

public interface IExcelWriter
{
    /// <param name="moneyColumns">One flag per header: true for a column of money (rounded to 2 decimals, shown as <c>#,##0.00</c>).</param>
    byte[] Write(string sheetName, string[] headers, IReadOnlyList<bool> moneyColumns, IEnumerable<object?[]> rows);
}
