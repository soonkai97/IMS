using FluentValidation;
namespace Ims.Application.Features.Reports;

public sealed class ExportStockSummaryValidator : AbstractValidator<ExportStockSummaryQuery>
{
    public ExportStockSummaryValidator() => RuleFor(x => x.Filter.Search).MustBeAShortSearch();
}
