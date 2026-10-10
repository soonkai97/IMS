using System.Globalization;
using Ims.Domain;
namespace Ims.Application.Features.Import;

/// <summary>Parsing and checks shared by the import handlers. Numbers are parsed with the invariant culture so a server locale cannot change the result.</summary>
public static class ImportSheet
{
    public static void RequireColumns(ExcelSheet sheet, params string[] required)
    {
        var missing = required.Where(r => !sheet.Headers.Contains(r)).ToList();
        if (missing.Count > 0) throw new ValidationException(Messages.MissingColumns(missing));
    }

    public static bool TryParseDecimal(string text, out decimal value) =>
        decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out value);

    /// <summary>Adds an error when <paramref name="text"/> is longer than the column allows (it would be truncated or rejected by the database).</summary>
    public static void CheckLength(List<string> errors, string field, string text, int max)
    {
        if (text.Length > max) errors.Add(Messages.TextTooLong(field, max));
    }

    /// <summary>D6: product and opening-stock imports are all-or-nothing, so nothing is written while any row is invalid.</summary>
    public static void EnsureNoErrors(IReadOnlyCollection<ImportRowResult> rows)
    {
        var invalid = rows.Count(r => !r.Ok);
        if (invalid > 0) throw new ValidationException(Messages.ImportHasErrors(invalid));
    }
}
