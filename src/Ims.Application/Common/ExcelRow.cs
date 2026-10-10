namespace Ims.Application;

public record ExcelRow(int Number, IReadOnlyDictionary<string, string> Cells)
{
    public string Get(string key) => Cells.GetValueOrDefault(key, "");
}
