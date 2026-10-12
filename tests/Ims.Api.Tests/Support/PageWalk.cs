using System.Text.Json;
using Ims.Api.Tests.Clients;
using Xunit.Sdk;

namespace Ims.Api.Tests.Support;

/// <summary>
/// Reads a cursor-paged list from the first page to the last and returns what <paramref name="read"/> makes of each row. The cursor is passed on as
/// opaque text, exactly as the server gave it; this is the only place that names the query parameter that carries it.
/// </summary>
public static class PageWalk
{
    private const string CursorParameter = "afterId";
    private const int MostPages = 50;

    /// <param name="url">A list URL that already has a query string (with the page size).</param>
    /// <param name="afterPage">Runs after each page that is not the last, with the page number: use it to change data between two pages.</param>
    public static async Task<List<T>> ReadAsync<T>(ApiClient who, string url, Func<JsonElement, T> read, Func<int, Task>? afterPage = null)
    {
        var rows = new List<T>();
        string? cursor = null;
        for (var page = 1; page <= MostPages; page++)
        {
            var response = await who.GetAsync(cursor is null ? url : $"{url}&{CursorParameter}={Uri.EscapeDataString(cursor)}");
            using var json = response.ShouldBeOk().Json();
            rows.AddRange(json.RootElement.GetProperty("items").EnumerateArray().Select(read));

            var next = json.RootElement.GetProperty("nextCursor");
            if (next.ValueKind == JsonValueKind.Null) return rows;
            cursor = next.ToString();
            if (afterPage is not null) await afterPage(page);
        }
        throw new XunitException($"The list {url} still had a next page after {MostPages} pages: the cursor does not advance.");
    }
}
