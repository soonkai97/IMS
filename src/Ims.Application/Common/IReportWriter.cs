namespace Ims.Application;

/// <summary>
/// Writes an Excel file for the caller: the columns they may not see (cost, value) are left out, header and cells alike. It is the only way a handler
/// writes a spreadsheet, so a new export cannot skip the cost rule; a test fails if anything else uses <see cref="IExcelWriter"/>.
/// </summary>
public interface IReportWriter
{
    /// <param name="rows">Every row has one cell per column of <paramref name="columns"/>, in the same order, including the restricted ones.</param>
    byte[] Write(string sheetName, IReadOnlyList<ReportColumn> columns, IEnumerable<object?[]> rows);
}
