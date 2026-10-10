namespace Ims.Application;

public static class ErrorKeys
{
    private static readonly string[] CommandWrappers = ["Data", "Body", "Filter"];

    /// <summary>
    /// Turns "Data.Lines[0].Qty" or "$.lines[0].qty" into "lines[0].qty": drops the command wrapper segment and JSON root marker and
    /// camel-cases each part, so clients see the same field names they send in the request body.
    /// </summary>
    public static string Normalize(string propertyName)
    {
        var parts = propertyName.Split('.').Where(p => p != "$").ToArray();
        var skip = parts.Length > 1 && CommandWrappers.Contains(parts[0]) ? 1 : 0;
        return string.Join('.', parts.Skip(skip).Select(p => p.Length == 0 ? p : char.ToLowerInvariant(p[0]) + p[1..]));
    }
}
