using FluentValidation;
namespace Ims.Application.Features.Catalog;

public class UpdateProductValidator : AbstractValidator<UpdateProductCommand>
{
    public UpdateProductValidator() => RuleFor(x => x.Data).SetValidator(new ProductSaveRequestValidator());
}
