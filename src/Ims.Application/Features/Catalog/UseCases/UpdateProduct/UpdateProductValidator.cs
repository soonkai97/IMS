using FluentValidation;
namespace Ims.Application.Features.Catalog;

public sealed class UpdateProductValidator : AbstractValidator<UpdateProductCommand>
{
    public UpdateProductValidator() => RuleFor(x => x.Data).SetValidator(new ProductSaveRequestValidator());
}
