using FluentValidation;
using Ims.Domain;
namespace Ims.Application.Features.Sales;

public class SetPriceListCustomerValidator : AbstractValidator<SetPriceListCustomerCommand>
{
    public SetPriceListCustomerValidator() => RuleFor(x => x.Data.CustomerId).NotEqual(Guid.Empty).WithMessage(Messages.ChooseCustomer);
}
