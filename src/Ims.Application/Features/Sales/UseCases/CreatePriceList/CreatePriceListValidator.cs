using FluentValidation;
namespace Ims.Application.Features.Sales;

public class CreatePriceListValidator : AbstractValidator<CreatePriceListCommand>
{
    public CreatePriceListValidator() => RuleFor(x => x.Data).SetValidator(new PriceListSaveRequestValidator());
}
