using FluentValidation;
using Ims.Domain;
namespace Ims.Application.Features.Catalog;

public class UomSaveValidator : AbstractValidator<UomSaveRequest>
{
    public UomSaveValidator()
    {
        RuleFor(x => x.Code).NotEmpty().WithMessage(Messages.UomCodeRequired).MaximumLength(Limits.CodeMaxLength).WithMessage(Messages.TextTooLong("Unit code", Limits.CodeMaxLength));
        RuleFor(x => x.Name).MaximumLength(Limits.UomNameMaxLength).WithMessage(Messages.TextTooLong("Unit name", Limits.UomNameMaxLength));
    }
}
