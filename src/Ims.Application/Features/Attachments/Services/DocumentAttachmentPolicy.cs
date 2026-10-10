using Ims.Application.Features.Documents;
using Ims.Domain;
namespace Ims.Application.Features.Attachments;

/// <summary>
/// Files on a stock document (for example the photo of a delivery note). Visible to anyone who may see the document's warehouse;
/// added by people who may create documents, also after posting (the paperwork often arrives later) but not to a cancelled document;
/// removed only while the document is a Draft.
/// </summary>
public class DocumentAttachmentPolicy(IReadRepository<StockDocument> documents, IDocumentAccess access, ICurrentUser current) : IAttachmentOwnerPolicy, IAppService
{
    public AttachmentOwnerType OwnerType => AttachmentOwnerType.Document;

    public async Task EnsureCanReadAsync(int ownerId, CancellationToken ct) => await LoadScopedAsync(ownerId, ct);

    public async Task EnsureCanWriteAsync(int ownerId, CancellationToken ct)
    {
        var scope = await LoadScopedAsync(ownerId, ct);
        RequireDocumentPermission();
        if (scope.Status == DocumentStatus.Cancelled) throw new ConflictException(Messages.AttachmentOnCancelled);
    }

    public async Task EnsureCanDeleteAsync(int ownerId, CancellationToken ct)
    {
        var scope = await LoadScopedAsync(ownerId, ct);
        RequireDocumentPermission();
        if (scope.Status != DocumentStatus.Draft) throw new ConflictException(Messages.AttachmentDeleteOnlyDraft);
    }

    private async Task<DocumentScope> LoadScopedAsync(int documentId, CancellationToken ct)
    {
        var scope = await documents.FirstOrDefaultAsync(new DocumentScopeSpec(documentId), ct) ?? throw new NotFoundException(Messages.DocumentNotFound);
        access.EnsureWarehouse(scope.WarehouseId);
        return scope;
    }

    private void RequireDocumentPermission()
    {
        if (!Permissions.Has(current.Role, Permissions.DocumentCreate)) throw new ForbiddenException(Messages.NotAllowed);
    }
}
