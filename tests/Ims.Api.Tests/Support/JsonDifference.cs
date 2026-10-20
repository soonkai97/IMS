using System.Text.Json;

namespace Ims.Api.Tests.Support;

/// <summary>
/// Finds the first place two JSON values differ. Object member order and white space do not count; array order does.
/// Line endings inside strings do not count either (CR LF equals LF): a document generated on Windows carries CR LF in its XML-doc texts, one built on Linux LF only.
/// </summary>
public static class JsonDifference
{
    /// <summary>The JSON path of the first difference, or null when both values are the same.</summary>
    public static string? First(JsonElement expected, JsonElement actual, string path = "$")
    {
        if (expected.ValueKind != actual.ValueKind) return $"{path}: {expected.ValueKind} vs {actual.ValueKind}";

        switch (expected.ValueKind)
        {
            case JsonValueKind.Object:
                var expectedMembers = expected.EnumerateObject().ToDictionary(p => p.Name, p => p.Value);
                var actualMembers = actual.EnumerateObject().ToDictionary(p => p.Name, p => p.Value);
                foreach (var name in expectedMembers.Keys.Except(actualMembers.Keys).Order()) return $"{path}.{name}: only in the committed file";
                foreach (var name in actualMembers.Keys.Except(expectedMembers.Keys).Order()) return $"{path}.{name}: only in the live document";
                return expectedMembers.OrderBy(m => m.Key, StringComparer.Ordinal)
                    .Select(m => First(m.Value, actualMembers[m.Key], $"{path}.{m.Key}"))
                    .FirstOrDefault(d => d is not null);
            case JsonValueKind.Array:
                var expectedItems = expected.EnumerateArray().ToList();
                var actualItems = actual.EnumerateArray().ToList();
                if (expectedItems.Count != actualItems.Count) return $"{path}: {expectedItems.Count} items vs {actualItems.Count}";
                return expectedItems.Select((item, i) => First(item, actualItems[i], $"{path}[{i}]")).FirstOrDefault(d => d is not null);
            case JsonValueKind.String:
                return Text(expected) == Text(actual) ? null : $"{path}: {expected.GetRawText()} vs {actual.GetRawText()}";
            default:
                return expected.GetRawText() == actual.GetRawText() ? null : $"{path}: {expected.GetRawText()} vs {actual.GetRawText()}";
        }
    }

    private static string Text(JsonElement value) => value.GetString()!.Replace("\r\n", "\n", StringComparison.Ordinal);
}
