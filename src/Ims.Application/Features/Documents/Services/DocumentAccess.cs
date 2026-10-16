using Ims.Domain;
namespace Ims.Application.Features.Documents;

/// <summary>Warehouse data scope for the current caller. What they may do with a document is decided by <see cref="DocumentActionPolicy"/>.</summary>
public class DocumentAccess(ICurrentUser current) : IDocumentAccess, IAppService
{
    public void EnsureWarehouse(int warehouseId)
    {
        if (!current.CanAccessWarehouse(warehouseId))
            throw new ValidationException(Messages.WarehouseMissing);
    }

    public void EnsureDocumentInScope(int warehouseId)
    {
        if (!current.CanAccessWarehouse(warehouseId))
            throw new NotFoundException(Messages.DocumentNotFound);
    }

    public IReadOnlyCollection<int>? WarehouseScope() => Roles.SeesAllWarehouses(current.Role) ? null : current.WarehouseIds;

    public void EnsureMayEditOrSubmit(StockDocument document)
    {
        if (!DocumentActionPolicy.MayEditOrSubmit(current.Role, current.UserId, document.ActionContext))
            throw new ForbiddenException(Messages.OwnDocumentsOnly);
    }

    public void EnsureMayCancel(StockDocument document)
    {
        if (!DocumentActionPolicy.MayCancel(current.Role, current.UserId, document.ActionContext))
            throw new ForbiddenException(document.Status switch
            {
                DocumentStatus.Draft => Messages.OwnDocumentsOnly,
                DocumentStatus.Approved => Messages.OnlyReviewerCanCancelApproved,
                _ => Messages.OwnSubmissionsOnly
            });
    }

    public void EnsureMayRecall(StockDocument document)
    {
        if (document.Status == DocumentStatus.Submitted && !DocumentActionPolicy.MayRecall(current.Role, current.UserId, document.ActionContext))
            throw new ForbiddenException(Messages.OwnRecallOnly);
    }

    public bool IsRepeatedRecall(StockDocument document) =>
        document is { Status: DocumentStatus.Draft, IsReturned: false } &&
        DocumentActionPolicy.MayEditOrSubmit(current.Role, current.UserId, document.ActionContext);

    public IReadOnlyList<string> AllowedActions(DocumentDto d, SystemSettings settings, bool withinApprovalLimit) =>
        DocumentActionPolicy.AllowedActions(current.Role, current.UserId,
            new DocumentActionContext(d.Status, d.RequiresApproval, d.IsLarge, d.CreatedByKey, d.SubmittedByKey), settings.AllowSelfApproval, withinApprovalLimit);

    public bool CanAct(DocumentListItem item, SystemSettings settings) =>
        DocumentActionPolicy.MayReview(current.Role, current.UserId,
            new DocumentActionContext(item.Status, item.RequiresApproval, item.IsLarge, item.CreatedByKey, item.SubmittedByKey), settings.AllowSelfApproval);
}
