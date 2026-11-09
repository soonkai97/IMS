using Ims.Domain;
namespace Ims.Application.Features.Returns;

/// <summary>The rules for saving and submitting a return (PL3.16), the twin of the delivery rules. The rules of a return line (it names an original line, with that line product and unit) are the line builder's.</summary>
public interface IReturnRules
{
    /// <summary>
    /// Loads the original of a Draft return for a save and checks the header: the draft is still editable, the caller may create returns and the Returns module is on, the original is still Posted, the partner and
    /// the purpose are the draft's (they are the original's and never change). The link itself is not a field of the request.
    /// </summary>
    /// <exception cref="ConflictException">The module is off, or the original is no longer Posted.</exception>
    /// <exception cref="ForbiddenException">The caller may not create returns.</exception>
    /// <exception cref="ValidationException">The partner or the purpose differs from the original.</exception>
    Task<StockDocument> OriginalForSaveAsync(StockDocument draft, Partner? partner, IssuePurpose? purpose, CancellationToken ct);

    /// <summary>The quantities of the lines, added up per original line, must not exceed what can still be returned of it. Drafts and Submitted returns reserve nothing, so this is the early answer for the person typing; the check that decides is made when a return posts, under the original lock.</summary>
    /// <exception cref="ValidationException">Names every original line that is over, with what can be returned (400).</exception>
    Task EnsureWithinRemainingAsync(StockDocument original, IReadOnlyList<StockDocumentLine> lines, CancellationToken ct);

    /// <summary>Submitting a return asks the same again: the module is on, the original is still Posted and every line is within what can be returned. Does nothing for a document that is not a return.</summary>
    Task EnsureCanSubmitAsync(StockDocument draft, CancellationToken ct);
}
