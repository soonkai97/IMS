namespace Ims.Application;

/// <param name="Cells">What each cell shows: the formatted text, trimmed (right for names, codes and barcodes: a cell formatted <c>00123</c> reads 00123).</param>
/// <param name="Numbers">For a cell that holds a NUMBER (typed, or the result of a formula), the number itself as invariant text, whatever format the cell shows.</param>
public record ExcelRow(int Number, IReadOnlyDictionary<string, string> Cells, IReadOnlyDictionary<string, string>? Numbers = null)
{
    public string Get(string key) => Cells.GetValueOrDefault(key, "");

    /// <summary>
    /// The value of a cell that is read as a number (a quantity, a cost): the number it holds, not the text it shows. A cell formatted to one decimal that holds 2.505
    /// shows 2.5; reading the text would let a fraction of a cent through (and reject 1,234.5 for its comma). A text cell reads as its text.
    /// </summary>
    public string GetNumber(string key) => Numbers?.GetValueOrDefault(key) ?? Get(key);
}
