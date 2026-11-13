using FluentValidation;
namespace Ims.Application.Features.Reports;

public sealed class ExportLedgerValidator : AbstractValidator<ExportLedgerQuery>
{
    public ExportLedgerValidator() => RuleFor(x => x.Filter.To).MustNotBeBefore(x => x.Filter.From);
}
