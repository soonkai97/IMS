using FluentValidation;
namespace Ims.Application.Features.Reports;

public class ExportMovementsValidator : AbstractValidator<ExportMovementsQuery>
{
    public ExportMovementsValidator() => RuleFor(x => x.Filter.To).MustNotBeBefore(x => x.Filter.From);
}
