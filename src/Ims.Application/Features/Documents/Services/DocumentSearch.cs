using System.Linq.Expressions;
using Ims.Domain;
namespace Ims.Application.Features.Documents;

/// <summary>
/// The text of the document search box turned into one predicate on the document: its number, its remark or its partner's name contains the text, ignoring case
/// (<see cref="IDocumentTextMatcher"/>). The validator has already refused a text under <see cref="Limits.SearchMinTextLength"/> characters. Nothing is truncated or
/// refused: every matching document is found, however many partners match the name.
/// </summary>
public sealed record DocumentSearch(string Text, Expression<Func<StockDocument, bool>> Matches)
{
    /// <summary>Null when there is no search text.</summary>
    public static async Task<DocumentSearch?> ResolveAsync(IDocumentTextMatcher matcher, string? raw, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var text = raw.Trim();
        return new DocumentSearch(text, await matcher.ContainsAsync(text, ct));
    }
}
