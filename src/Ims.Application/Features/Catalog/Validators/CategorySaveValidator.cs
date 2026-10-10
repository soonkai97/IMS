using FluentValidation;
using Ims.Domain;
namespace Ims.Application.Features.Catalog;

public class CategorySaveValidator : AbstractValidator<CategorySaveRequest>
{
    public CategorySaveValidator() => RuleFor(x => x.Name).NotEmpty().MaximumLength(Limits.ShortNameMaxLength).WithMessage(Messages.CategoryNameRequired);
}
