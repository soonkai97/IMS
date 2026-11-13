using FluentValidation;
namespace Ims.Application.Features.Reports;

public sealed class ListMovementsValidator : AbstractValidator<ListMovementsQuery>
{
    public ListMovementsValidator() => RuleFor(x => x.Filter.To).MustNotBeBefore(x => x.Filter.From);
}
