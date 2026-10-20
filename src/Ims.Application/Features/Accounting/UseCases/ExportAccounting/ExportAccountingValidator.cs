using FluentValidation;
using Ims.Domain;
namespace Ims.Application.Features.Accounting;

public class ExportAccountingValidator : AbstractValidator<ExportAccountingQuery>
{
    public ExportAccountingValidator()
    {
        RuleFor(x => x.Filter.From).NotNull().WithMessage(Messages.AccountingRangeRequired);
        RuleFor(x => x.Filter.To).NotNull().WithMessage(Messages.AccountingRangeRequired).MustNotBeBefore(x => x.Filter.From);
    }
}
