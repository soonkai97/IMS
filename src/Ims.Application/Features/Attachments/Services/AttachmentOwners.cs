using Ims.Domain;
namespace Ims.Application.Features.Attachments;

public class AttachmentOwners(IEnumerable<IAttachmentOwnerPolicy> policies) : IAttachmentOwners, IAppService
{
    private readonly Dictionary<AttachmentOwnerType, IAttachmentOwnerPolicy> _byType = policies.ToDictionary(p => p.OwnerType);

    public IAttachmentOwnerPolicy For(AttachmentOwnerType ownerType) =>
        _byType.TryGetValue(ownerType, out var p) ? p : throw new ValidationException(Messages.AttachmentNotFound);
}
