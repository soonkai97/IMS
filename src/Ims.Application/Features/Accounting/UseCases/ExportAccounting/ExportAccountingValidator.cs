using FluentValidation;
using Ims.Domain;
namespace Ims.Application.Features.Accounting;

public class ExportAccountingValidator : AbstractValidator<ExportAccountingQuery>
{
    public ExportAccountingValidator()
    {
        // The bounds keep the day arithmetic of the query (a Kuala Lumpur day in UTC, plus one day) away from the ends of the calendar, which would overflow into a 500.
        RuleFor(x => x.Filter.From).NotNull().WithMessage(Messages.AccountingRangeRequired).Must(WithinBounds).WithMessage(Messages.AccountingDateOutOfRange);
        RuleFor(x => x.Filter.To).NotNull().WithMessage(Messages.AccountingRangeRequired).Must(WithinBounds).WithMessage(Messages.AccountingDateOutOfRange)
            .MustNotBeBefore(x => x.Filter.From);
    }

    private static bool WithinBounds(DateOnly? date) => date is null || (date >= AccountingLayout.EarliestDate && date <= AccountingLayout.LatestDate);
}
