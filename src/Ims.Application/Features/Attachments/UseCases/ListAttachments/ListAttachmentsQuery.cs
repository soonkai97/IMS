using Ims.Domain;
namespace Ims.Application.Features.Attachments;

public sealed record ListAttachmentsQuery(AttachmentOwnerType OwnerType, Guid OwnerId) : IQuery<IReadOnlyList<AttachmentDto>>;
