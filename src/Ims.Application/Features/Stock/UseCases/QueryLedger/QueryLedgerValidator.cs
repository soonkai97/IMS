using FluentValidation;
namespace Ims.Application.Features.Stock;

public class QueryLedgerValidator : AbstractValidator<QueryLedgerQuery>
{
    public QueryLedgerValidator()
    {
        RuleFor(x => x.Filter.Cursor).MustBeAValidCursor();
        RuleFor(x => x.Filter.To).MustNotBeBefore(x => x.Filter.From);
    }
}
