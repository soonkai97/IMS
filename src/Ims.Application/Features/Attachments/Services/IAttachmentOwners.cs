using Ims.Domain;
namespace Ims.Application.Features.Attachments;

public interface IAttachmentOwners
{
    IAttachmentOwnerPolicy For(AttachmentOwnerType ownerType);
}
