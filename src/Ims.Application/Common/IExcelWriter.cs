namespace Ims.Application;

public interface IExcelWriter
{
    byte[] Write(string sheetName, string[] headers, IEnumerable<object?[]> rows);
}
