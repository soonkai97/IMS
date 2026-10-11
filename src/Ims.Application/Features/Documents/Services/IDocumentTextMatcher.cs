using System.Linq.Expressions;
using Ims.Domain;
namespace Ims.Application.Features.Documents;

/// <summary>
/// The "contains" test of the document search box, as an expression the database evaluates. It lives behind an interface because the fast form needs a
/// database-specific collation (Infrastructure); the Application only asks "does this document contain the text".
/// </summary>
public interface IDocumentTextMatcher
{
    /// <summary>
    /// True when the document's number or its remark contains <paramref name="text"/> (ignoring case), or its partner is one of the partners whose name contains it.
    /// </summary>
    /// <param name="matchingPartnerIds">
    /// The partners whose name contains the text, when there are few of them: tested as <c>PartnerId IN (..)</c>, the cheapest form for a short list. Null when
    /// there are too many for a list (<see cref="Limits.SearchPartnerListMax"/>): the partner name is then tested through a join, which costs the same whatever
    /// the number of matching partners. Either way every matching document is found; nothing is cut off.
    /// </param>
    Expression<Func<StockDocument, bool>> Contains(string text, IReadOnlyCollection<int>? matchingPartnerIds);
}
