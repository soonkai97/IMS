using FluentValidation;
using Ims.Domain;
namespace Ims.Application.Features.Sales;

public class LookupPriceValidator : AbstractValidator<LookupPriceQuery>
{
    public LookupPriceValidator()
    {
        RuleFor(x => x.Filter.CustomerId).NotEqual(Guid.Empty).WithMessage(Messages.ChooseCustomer);
        RuleFor(x => x.Filter.ProductId).NotEqual(Guid.Empty).WithMessage(Messages.ChooseProduct);
    }
}
