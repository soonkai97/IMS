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
    /// True when the document's number or its remark contains <paramref name="text"/>, or its partner's name does, all compared the same way (ignoring case). The
    /// partners are looked up first: a short list (<see cref="Limits.SearchPartnerListMax"/>) is tested as an id list on the document, the cheapest form; with more
    /// matching partners the name is tested through a join instead, which costs the same whatever the number of matches. Both forms find exactly the same documents;
    /// nothing is cut off.
    /// </summary>
    Task<Expression<Func<StockDocument, bool>>> ContainsAsync(string text, CancellationToken ct);
}
