using Ims.Domain;
namespace Ims.Application.Features.Attachments;

/// <summary>
/// Who may see, add and remove files of one kind of owner. One implementation per <see cref="AttachmentOwnerType"/>;
/// a new owner type adds one class and touches nothing else.
/// </summary>
public interface IAttachmentOwnerPolicy
{
    AttachmentOwnerType OwnerType { get; }

    /// <summary>
    /// The integer key of the owner a client named by public id, looked up among the owners of THIS type: an id that is unknown, soft-deleted or the id of another kind
    /// of row (a product id sent for a document) gives <see cref="IdMapExtensions.NoRow"/>, which the checks below refuse with the same 404 as any missing owner,
    /// after the permission check, so the answer never tells an existing id from a missing one.
    /// </summary>
    Task<int> ResolveAsync(Guid ownerPublicId, CancellationToken ct);

    Task EnsureCanReadAsync(int ownerId, CancellationToken ct);
    Task EnsureCanWriteAsync(int ownerId, CancellationToken ct);
    Task EnsureCanDeleteAsync(int ownerId, CancellationToken ct);
}
