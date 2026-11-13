using FluentValidation;
namespace Ims.Application.Features.Catalog;

public sealed class UpdateCategoryValidator : AbstractValidator<UpdateCategoryCommand>
{
    public UpdateCategoryValidator() => RuleFor(x => x.Data).SetValidator(new CategorySaveValidator());
}
