using Ims.Domain;
namespace Ims.Application.Features.Attachments;

/// <summary>
/// Who may see, add and remove files of one kind of owner. One implementation per <see cref="AttachmentOwnerType"/>;
/// a new owner type adds one class and touches nothing else.
/// </summary>
public interface IAttachmentOwnerPolicy
{
    AttachmentOwnerType OwnerType { get; }
    Task EnsureCanReadAsync(int ownerId, CancellationToken ct);
    Task EnsureCanWriteAsync(int ownerId, CancellationToken ct);
    Task EnsureCanDeleteAsync(int ownerId, CancellationToken ct);
}
