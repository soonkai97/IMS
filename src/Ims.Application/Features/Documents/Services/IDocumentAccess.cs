using Ims.Domain;
namespace Ims.Application.Features.Documents;

/// <summary>Who may see and do what. Warehouse data scope, the ownership guards of the commands, and the action list the UI renders.</summary>
public interface IDocumentAccess
{
    /// <summary>A warehouse outside the caller's scope is answered like one that does not exist (400, same message), so warehouse ids cannot be probed.</summary>
    /// <exception cref="ValidationException">The warehouse chosen for a new document is outside the caller's scope.</exception>
    void EnsureWarehouse(int warehouseId);

    /// <summary>An existing document outside the caller's warehouses does not exist as far as they can tell: 404, not 403, so document ids cannot be probed.</summary>
    /// <exception cref="NotFoundException">The document is in a warehouse the caller cannot access.</exception>
    void EnsureDocumentInScope(int warehouseId);
    /// <summary>Null = the caller sees every warehouse.</summary>
    IReadOnlyCollection<int>? WarehouseScope();

    /// <exception cref="ForbiddenException">Warehouse staff and Clerk may edit and submit only documents they created (UX 7.2).</exception>
    void EnsureMayEditOrSubmit(StockDocument document);

    /// <exception cref="ForbiddenException">Warehouse staff and Clerk may cancel only their own Drafts and the documents they submitted (UX 7.2); an Approved document only a reviewer it is routed to.</exception>
    void EnsureMayCancel(StockDocument document);

    /// <summary>
    /// A document is taken back only by its submitter. A Draft counts as theirs too when they made it or last changed it (a recall names no submitter
    /// afterwards, so a retry must still be theirs). Anyone else is refused in every status, so the answer does not tell what state it is in.
    /// </summary>
    /// <exception cref="ForbiddenException">The caller did not submit the document.</exception>
    void EnsureMayRecall(StockDocument document);

    /// <summary>
    /// True when a recall would repeat one that already happened: the document is an ordinary Draft (not a returned one). Asked after
    /// <see cref="EnsureMayRecall"/>, so only the caller's own; the recall then answers with the document and changes nothing.
    /// </summary>
    bool IsRepeatedRecall(StockDocument document);

    /// <param name="withinApprovalLimit">False when the approval rules in force need a higher role than the caller's: approve and reject are then not offered.</param>
    IReadOnlyList<string> AllowedActions(DocumentDto document, SystemSettings settings, bool withinApprovalLimit);

    /// <summary>True when the caller may approve or reject this listed document now (it is routed to their role and is not their own submission).</summary>
    bool CanAct(DocumentListItem item, SystemSettings settings);
}
