namespace Ims.Application;

public class ReportWriter(IExcelWriter excel, ICallerPermissions caller) : IReportWriter, IAppService
{
    public byte[] Write(string sheetName, IReadOnlyList<ReportColumn> columns, IEnumerable<object?[]> rows)
    {
        var visible = Enumerable.Range(0, columns.Count).Where(i => columns[i].VisibleWith is not { } permission || caller.Has(permission)).ToArray();
        return excel.Write(sheetName, visible.Select(i => columns[i].Header).ToArray(), rows.Select(row => visible.Select(i => row[i]).ToArray()));
    }
}
