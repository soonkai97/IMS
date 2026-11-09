using FluentValidation;
namespace Ims.Application.Features.Sales;

public class UpdatePriceListValidator : AbstractValidator<UpdatePriceListCommand>
{
    public UpdatePriceListValidator() => RuleFor(x => x.Data).SetValidator(new PriceListSaveRequestValidator());
}
