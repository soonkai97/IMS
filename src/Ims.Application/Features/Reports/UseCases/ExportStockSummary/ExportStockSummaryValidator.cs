using FluentValidation;
namespace Ims.Application.Features.Reports;

public class ExportStockSummaryValidator : AbstractValidator<ExportStockSummaryQuery>
{
    public ExportStockSummaryValidator() => RuleFor(x => x.Filter.Search).MustBeAShortSearch();
}
