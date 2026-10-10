using FluentValidation;
namespace Ims.Application.Features.Catalog;

public class CreateProductValidator : AbstractValidator<CreateProductCommand>
{
    public CreateProductValidator() => RuleFor(x => x.Data).SetValidator(new ProductSaveRequestValidator());
}
