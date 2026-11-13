using FluentValidation;
namespace Ims.Application.Features.Reports;

public sealed class ExportMovementsValidator : AbstractValidator<ExportMovementsQuery>
{
    public ExportMovementsValidator() => RuleFor(x => x.Filter.To).MustNotBeBefore(x => x.Filter.From);
}
