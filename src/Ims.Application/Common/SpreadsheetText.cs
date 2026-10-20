namespace Ims.Application;

/// <summary>
/// Text that came from a person (a partner name, a SKU, a tax code) is data in a spreadsheet, never a formula. A cell whose text starts with <c>=</c>, <c>+</c>, <c>-</c>, <c>@</c>, a tab or a
/// carriage return can run as a formula in a tool that reads the file or a copy of it (formula injection, OWASP "CSV injection"); it gets a leading single quote, the standard neutralisation.
/// In an .xlsx the quote is Excel's own text marker: the writer (ClosedXML) drops it from the stored text and sets the cell's quote-prefix flag, so the value is unchanged for an import and
/// no spreadsheet runs it, even when somebody edits the cell (the API test of the accounting export pins this in the file). Numbers and dates are not text and are never passed through here.
/// </summary>
public static class SpreadsheetText
{
    private const char Quote = '\'';
    private static readonly char[] Triggers = ['=', '+', '-', '@', '\t', '\r'];

    /// <summary>The text as it is written to a cell: unchanged unless it could run as a formula. Null stays null (an empty cell).</summary>
    public static string? Safe(string? text) => text is { Length: > 0 } && Triggers.Contains(text[0]) ? Quote + text : text;
}
