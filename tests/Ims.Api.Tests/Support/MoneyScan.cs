using System.Text.Json;
using System.Text.RegularExpressions;

namespace Ims.Api.Tests.Support;

/// <summary>
/// Finds cost and value in a response by NAME, independently of the production attribute: every property whose name looks like money and whose value
/// is not null, including the keys inside the audit log's before/after JSON text. The cost tests compare the result with what a role may see, so a
/// value that leaks (or a new money field nobody listed) fails whatever endpoint carries it.
/// </summary>
public static partial class MoneyScan
{
    [GeneratedRegex("cost|amount|price|worth|valu|threshold|actual", RegexOptions.IgnoreCase)]
    private static partial Regex Looks();

    /// <summary>Names that match but are not money.</summary>
    private static readonly HashSet<string> NotMoney = new(StringComparer.OrdinalIgnoreCase) { "amountIsEstimate", "qtyThreshold" };

    public static bool IsMoneyName(string name) => Looks().IsMatch(name) && !NotMoney.Contains(name);

    /// <summary>The distinct, lower-case names of the money fields that carry a value.</summary>
    public static SortedSet<string> InJson(string json)
    {
        var found = new SortedSet<string>(StringComparer.Ordinal);
        using var document = JsonDocument.Parse(json);
        Walk(document.RootElement, found);
        return found;
    }

    public static SortedSet<string> InJson(Harness.ApiResponse response) => InJson(response.Body);

    /// <summary>
    /// Every money field of the JSON whose number has a fraction of a cent ("unitCost=10.005"). Trailing zeros are not a fraction (10.0100 is fine), and the audit
    /// log's before/after text is looked into. A quantity or a count is not money and is never listed.
    /// </summary>
    public static List<string> SubCentValues(string json)
    {
        var found = new List<string>();
        using var document = JsonDocument.Parse(json);
        FindSubCent(document.RootElement, found);
        return found;
    }

    /// <summary>The fields that are unit prices (a derived cost per base unit): 4 decimals. The JSON name <c>unitCost</c> is shared with the entered cost of a document line (2 decimals), which other tests pin.</summary>
    private static readonly HashSet<string> UnitPriceNames = new(StringComparer.OrdinalIgnoreCase) { "avgCost", "unitCost", "baseUnitCost" };

    private static void FindSubCent(JsonElement element, List<string> found)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                // The numbers of a quantity rule (threshold, actual) are quantities, not money.
                var isQuantityTrigger = element.TryGetProperty("metric", out var metric) && metric.ValueKind == JsonValueKind.String && metric.GetString() == "Quantity";
                foreach (var property in element.EnumerateObject())
                {
                    if (isQuantityTrigger && property.Name is "threshold" or "actual") continue;
                    var places = UnitPriceNames.Contains(property.Name) ? 4 : 2;
                    var isPayload = property.Name is "before" or "after" && property.Value.ValueKind == JsonValueKind.String;
                    if (isPayload && TryParse(property.Value.GetString()) is { } embedded) FindSubCent(embedded, found);
                    else if (property.Value.ValueKind == JsonValueKind.Number && IsMoneyName(property.Name) && property.Value.TryGetDecimal(out var number) && Math.Round(number, places) != number)
                        found.Add($"{property.Name}={property.Value.GetRawText()}");
                    else FindSubCent(property.Value, found);
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray()) FindSubCent(item, found);
                break;
        }
    }

    /// <summary>The header cells of a workbook that name money.</summary>
    public static SortedSet<string> InWorkbook(byte[] bytes) =>
        new(Xlsx.Read(bytes).Headers.Where(IsMoneyName).Select(h => h.ToLowerInvariant()), StringComparer.Ordinal);

    public static SortedSet<string> Names(params string[] names) => new(names.Select(n => n.ToLowerInvariant()), StringComparer.Ordinal);

    private static void Walk(JsonElement element, SortedSet<string> found)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (property.Value.ValueKind == JsonValueKind.Null) continue;
                    if (IsMoneyName(property.Name)) found.Add(property.Name.ToLowerInvariant());
                    var isPayload = property.Name is "before" or "after" && property.Value.ValueKind == JsonValueKind.String;
                    if (isPayload && TryParse(property.Value.GetString()) is { } embedded) Walk(embedded, found);
                    else Walk(property.Value, found);
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray()) Walk(item, found);
                break;
        }
    }

    private static JsonElement? TryParse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        try { return JsonDocument.Parse(text).RootElement.Clone(); }
        catch (JsonException) { return null; }
    }
}
