using System.Linq.Expressions;
using Ims.Application.Features.Documents;
using Ims.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ims.Infrastructure.Persistence;

/// <summary>
/// The contains-match of the document search box in its fast form: both sides are lower-cased and compared with a BINARY collation. A case-insensitive LIKE
/// '%x%' under the database's linguistic collation costs about ten times as much CPU as the same scan compared by code point (measured on 1M documents:
/// 3.1 s against 0.41 s for a term that matches nothing), and a contains-match must read every row either way. Lower-casing first keeps it case-insensitive.
/// The partners are matched with the very same comparison, so the result never depends on how many of them match. Values are parameters, special characters are
/// escaped by EF (<c>Contains</c>); no raw SQL.
/// </summary>
public sealed class EfDocumentTextMatcher(AppDbContext db) : IDocumentTextMatcher
{
    /// <summary>A code-point collation that is available on every SQL Server version the application supports.</summary>
    public const string BinaryCollation = "Latin1_General_100_BIN2";

    public async Task<Expression<Func<StockDocument, bool>>> ContainsAsync(string text, CancellationToken ct)
    {
        var lowered = text.ToLowerInvariant();

        // One more than the list limit tells "a short list" from "too many for a list".
        var partnerIds = await db.Set<Partner>().AsNoTracking()
            .Where(p => EF.Functions.Collate(p.Name.ToLower(), BinaryCollation).Contains(lowered))
            .OrderBy(p => p.Id).Take(Limits.SearchPartnerListMax + 1).Select(p => p.Id).ToListAsync(ct);

        if (partnerIds.Count > Limits.SearchPartnerListMax)
            return d => EF.Functions.Collate(d.No.ToLower(), BinaryCollation).Contains(lowered)
                || (d.Remark != null && EF.Functions.Collate(d.Remark.ToLower(), BinaryCollation).Contains(lowered))
                || (d.Partner != null && EF.Functions.Collate(d.Partner.Name.ToLower(), BinaryCollation).Contains(lowered));

        // A short list of partners: the ids are tested on the document itself, so no join is added to the scan.
        return d => EF.Functions.Collate(d.No.ToLower(), BinaryCollation).Contains(lowered)
            || (d.Remark != null && EF.Functions.Collate(d.Remark.ToLower(), BinaryCollation).Contains(lowered))
            || (d.PartnerId != null && partnerIds.Contains(d.PartnerId.Value));
    }
}
