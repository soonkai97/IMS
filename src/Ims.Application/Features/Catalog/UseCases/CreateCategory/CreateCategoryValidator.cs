using FluentValidation;
namespace Ims.Application.Features.Catalog;

public class CreateCategoryValidator : AbstractValidator<CreateCategoryCommand>
{
    public CreateCategoryValidator() => RuleFor(x => x.Data).SetValidator(new CategorySaveValidator());
}
