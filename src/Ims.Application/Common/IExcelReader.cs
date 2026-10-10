namespace Ims.Application;

public interface IExcelReader
{
    /// <exception cref="ValidationException">The stream is not a readable workbook, or has more than <paramref name="maxRows"/> data rows.</exception>
    ExcelSheet Read(Stream file, int maxRows);
}
