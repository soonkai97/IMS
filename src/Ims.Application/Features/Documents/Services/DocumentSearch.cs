using System.Linq.Expressions;
using Ims.Domain;
namespace Ims.Application.Features.Documents;

/// <summary>
/// The text of the document search box turned into one predicate on the document. From <see cref="Limits.SearchMinTextLength"/> characters on, a document matches
/// when its number, its remark or its partner's name contains the text (<see cref="IDocumentTextMatcher"/>). A shorter text only matches the START of the
/// document number, a seek on the number index: a one- or two-letter contains-match on a million documents would read every remark and name for matches nobody
/// can use. Nothing is truncated or refused: every matching document is found, however many partners match the name.
/// </summary>
public sealed record DocumentSearch(string Text, Expression<Func<StockDocument, bool>> Matches)
{
    /// <summary>Null when there is no search text.</summary>
    public static async Task<DocumentSearch?> ResolveAsync(IReadRepository<Partner> partners, IDocumentTextMatcher matcher, string? raw, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var text = raw.Trim();
        if (text.Length < Limits.SearchMinTextLength) return new DocumentSearch(text, d => d.No.StartsWith(text));

        // A short list of partners is tested as an id list; a long one is not read further and the name is tested through a join (see IDocumentTextMatcher).
        var ids = await partners.ListAsync(new PartnerIdsByNameSpec(text), ct);
        return new DocumentSearch(text, matcher.Contains(text, ids.Count <= Limits.SearchPartnerListMax ? ids : null));
    }
}
