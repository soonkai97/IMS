using FluentValidation;
namespace Ims.Application.Features.Catalog;

public sealed class CreateProductValidator : AbstractValidator<CreateProductCommand>
{
    public CreateProductValidator() => RuleFor(x => x.Data).SetValidator(new ProductSaveRequestValidator());
}
