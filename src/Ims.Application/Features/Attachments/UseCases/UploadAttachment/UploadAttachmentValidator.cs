using FluentValidation;
using Ims.Domain;
namespace Ims.Application.Features.Attachments;

public class UploadAttachmentValidator : AbstractValidator<UploadAttachmentCommand>
{
    public UploadAttachmentValidator()
    {
        RuleFor(x => x.OwnerType).IsInEnum();
        RuleFor(x => x.OwnerId).GreaterThan(0);
        RuleFor(x => x.FileName).NotEmpty().WithMessage(Messages.FileNameRequired);
        RuleFor(x => x.Length).GreaterThan(0).WithMessage(Messages.FileEmpty)
            .LessThanOrEqualTo(Limits.MaxAttachmentBytes).WithMessage(Messages.FileTooLarge(Limits.MaxAttachmentBytes));
    }
}
