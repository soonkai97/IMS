using System.Globalization;
using Ims.Domain;
namespace Ims.Application.Features.Sales;

/// <summary>The words of a price list's audit events: the header and item count, and in words the prices a save added, removed or changed (a gated key, see <see cref="AuditFields.ListPriceChanges"/>).</summary>
internal static class ListAudit
{
    /// <summary>At most this many changed prices are written in words; the rest are counted. A save may touch thousands of items and the audit row stays small.</summary>
    private const int MaxChangesInWords = 50;

    /// <summary>An item as it was before a save: a kept item is repriced in place, so what it was has to be read first.</summary>
    internal record Priced(int ProductId, int UomId, decimal Price, string Sku, string UomCode);

    public static Dictionary<string, object?> Summary(PriceList list) => new()
    {
        [nameof(list.Name)] = list.Name,
        [nameof(list.IsDefault)] = list.IsDefault,
        [nameof(list.CustomerId)] = list.CustomerId,
        ["Items"] = list.Items.Count
    };

    /// <summary>The items of a list that has been read with each item's product and unit.</summary>
    public static List<Priced> Snapshot(PriceList list) => list.Items.Select(i => new Priced(i.ProductId, i.UomId, i.ListPrice, i.Product.Sku, i.Uom.Code)).ToList();

    /// <summary>The summary of a list that was just saved, with what its prices changed from <paramref name="before"/>.</summary>
    public static Dictionary<string, object?> Summary(PriceList list, IReadOnlyCollection<Priced> before, BuiltPriceListItems built)
    {
        var summary = Summary(list);
        var words = Changes(before, list.Items.Select(i => new Priced(i.ProductId, i.UomId, i.ListPrice, built.Skus.GetValueOrDefault(i.ProductId, ""), built.UomCodes.GetValueOrDefault(i.UomId, ""))).ToList());
        if (words.Count > 0) summary[AuditFields.ListPriceChanges] = words;
        return summary;
    }

    /// <summary>
    /// The summary of a list that is being deleted, with its prices in words (the first 50, then a count). The prices are not lost with the audit's cap: a deleted list and its items are soft-deleted, so every
    /// price stays in the database, and a restore is a data fix, not a reconstruction from the audit log.
    /// </summary>
    public static Dictionary<string, object?> Deleted(PriceList list)
    {
        var summary = Summary(list);
        var words = Changes(Snapshot(list), []);
        if (words.Count > 0) summary[AuditFields.ListPriceChanges] = words;
        return summary;
    }

    private static List<string> Changes(IReadOnlyCollection<Priced> before, IReadOnlyCollection<Priced> after)
    {
        var was = before.ToDictionary(i => (i.ProductId, i.UomId));
        var now = after.ToDictionary(i => (i.ProductId, i.UomId));
        static string Name(Priced i) => $"{i.Sku} ({i.UomCode})";
        static string Money(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);

        var lines = new List<string>();
        lines.AddRange(now.Values.Where(i => !was.ContainsKey((i.ProductId, i.UomId))).Select(i => $"{Name(i)}: added at {Money(i.Price)}"));
        lines.AddRange(was.Values.Where(i => !now.ContainsKey((i.ProductId, i.UomId))).Select(i => $"{Name(i)}: removed (was {Money(i.Price)})"));
        lines.AddRange(now.Values.Where(i => was.TryGetValue((i.ProductId, i.UomId), out var old) && old.Price != i.Price)
            .Select(i => $"{Name(i)}: changed from {Money(was[(i.ProductId, i.UomId)].Price)} to {Money(i.Price)}"));
        if (lines.Count > MaxChangesInWords) lines = [.. lines.Take(MaxChangesInWords), $"... and {lines.Count - MaxChangesInWords} more"];
        return lines;
    }
}
