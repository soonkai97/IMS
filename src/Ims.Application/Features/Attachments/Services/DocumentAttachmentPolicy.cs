using Ims.Application.Features.Documents;
using Ims.Domain;
namespace Ims.Application.Features.Attachments;

/// <summary>
/// Files on a stock document (for example the photo of a delivery note). Visible to anyone who may read documents (<c>stock.read</c>) in the document's warehouse;
/// added by people who may create documents, also after posting (the paperwork often arrives later) but not to a cancelled document;
/// removed only while the document is a Draft.
/// </summary>
public class DocumentAttachmentPolicy(IReadRepository<StockDocument> documents, IIdMap<StockDocument> documentIds, IDocumentAccess access, ICurrentUser current)
    : IAttachmentOwnerPolicy, IAppService
{
    public AttachmentOwnerType OwnerType => AttachmentOwnerType.Document;

    public async Task<int> ResolveAsync(Guid ownerPublicId, CancellationToken ct) =>
        await documentIds.ToInternalOrNoRowAsync(ownerPublicId, ct) ?? IdMapExtensions.NoRow;

    public async Task EnsureCanReadAsync(int ownerId, CancellationToken ct)
    {
        Require(Permissions.StockRead); // the permission that opens the document itself: its files are not more public than it is
        await LoadScopedAsync(ownerId, ct);
    }

    public async Task EnsureCanWriteAsync(int ownerId, CancellationToken ct)
    {
        Require(Permissions.DocumentCreate); // before the look-up: without the permission every id answers 403
        var scope = await LoadScopedAsync(ownerId, ct);
        if (scope.Status == DocumentStatus.Cancelled) throw new ConflictException(Messages.AttachmentOnCancelled);
    }

    public async Task EnsureCanDeleteAsync(int ownerId, CancellationToken ct)
    {
        Require(Permissions.DocumentCreate);
        var scope = await LoadScopedAsync(ownerId, ct);
        if (scope.Status != DocumentStatus.Draft) throw new ConflictException(Messages.AttachmentDeleteOnlyDraft);
    }

    private async Task<DocumentScope> LoadScopedAsync(int documentId, CancellationToken ct)
    {
        var scope = await documents.FirstOrDefaultAsync(new DocumentScopeSpec(documentId), ct) ?? throw new NotFoundException(Messages.DocumentNotFound);
        access.EnsureDocumentInScope(scope.WarehouseId); // 404 like a missing document, never a 403 that confirms the id exists (SEC-11)
        return scope;
    }

    private void Require(string permission)
    {
        if (!current.Rights().Has(permission)) throw new ForbiddenException(Messages.NotAllowed);
    }
}
