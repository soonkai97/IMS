using FluentValidation;
using Ims.Domain;
namespace Ims.Application.Features.Catalog;

public class UomSaveValidator : AbstractValidator<UomSaveRequest>
{
    public UomSaveValidator() => RuleFor(x => x.Code).NotEmpty().MaximumLength(Limits.CodeMaxLength).WithMessage(Messages.UomCodeRequired);
}
