using System.Globalization;

namespace Ims.Domain.Tests.Stock.Oracle;

/// <summary>How a value is written into a fixture row: invariant culture, no trailing zeros (a refactor that returns 3.0 for 3 is not a change of behaviour), a comma-safe text field.</summary>
internal static class OracleCsv
{
    private const string NumberFormat = "0.############################";

    public static string Number(decimal value) => value.ToString(NumberFormat, CultureInfo.InvariantCulture);

    public static string Number(decimal? value) => value is { } v ? Number(v) : "";

    public static string Number(int? value) => value is { } v ? v.ToString(CultureInfo.InvariantCulture) : "";

    public static string Flag(bool value) => value ? "1" : "0";

    public static string Field(string text) => text.IndexOfAny([',', '"', '\n', '\r']) < 0 ? text : $"\"{text.Replace("\"", "\"\"")}\"";

    public static string Row(IEnumerable<string> fields) => string.Join(',', fields.Select(Field));
}
