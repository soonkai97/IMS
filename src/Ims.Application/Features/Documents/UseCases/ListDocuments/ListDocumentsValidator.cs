using FluentValidation;
namespace Ims.Application.Features.Documents;

public class ListDocumentsValidator : AbstractValidator<ListDocumentsQuery>
{
    public ListDocumentsValidator()
    {
        RuleFor(x => x.Filter.Search).MustBeAShortSearch();
        RuleFor(x => x.Filter.To).MustNotBeBefore(x => x.Filter.From);
    }
}
