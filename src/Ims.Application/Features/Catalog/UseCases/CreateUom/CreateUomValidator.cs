using FluentValidation;
namespace Ims.Application.Features.Catalog;

public sealed class CreateUomValidator : AbstractValidator<CreateUomCommand>
{
    public CreateUomValidator() => RuleFor(x => x.Data).SetValidator(new UomSaveValidator());
}
