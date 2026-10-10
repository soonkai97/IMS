using FluentValidation;
namespace Ims.Application.Features.Catalog;

public class UpdateUomValidator : AbstractValidator<UpdateUomCommand>
{
    public UpdateUomValidator() => RuleFor(x => x.Data).SetValidator(new UomSaveValidator());
}
