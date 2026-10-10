using FluentValidation;
using Ims.Domain;
namespace Ims.Application.Features.Import;

public class ImportTemplateValidator : AbstractValidator<GetImportTemplateQuery>
{
    public ImportTemplateValidator() =>
        RuleFor(x => x.Kind).Must(k => k is ImportKinds.Products or ImportKinds.OpeningStock).WithMessage(Messages.UnknownTemplate);
}
