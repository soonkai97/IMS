using Ims.Domain;
namespace Ims.Application.Features.Documents;

/// <summary>
/// The text of the document search box plus the partners whose name contains it. A document matches when its number or its remark contains the text, or
/// its partner is one of those partners. The partners are resolved BEFORE the list query is built, so the document predicate is
/// <c>No LIKE .. OR Remark LIKE .. OR PartnerId IN (..)</c>: three columns of the same table, which one narrow index answers (<c>IX_StockDocument_Search</c>),
/// never an <c>EXISTS</c> on another table inside the OR (SQL Server would probe it once per document).
/// </summary>
public sealed record DocumentSearch(string Text, IReadOnlyCollection<int> PartnerIds)
{
    /// <summary>Null when there is no search text.</summary>
    public static async Task<DocumentSearch?> ResolveAsync(IReadRepository<Partner> partners, string? raw, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var text = raw.Trim();
        return new DocumentSearch(text, await partners.ListAsync(new PartnerIdsByNameSpec(text), ct));
    }
}
