using FluentValidation;
namespace Ims.Application.Features.Sales;

public class CreateDeliveryFromSalesOrderValidator : AbstractValidator<CreateDeliveryFromSalesOrderCommand>
{
    public CreateDeliveryFromSalesOrderValidator() => RuleFor(x => x.OrderId).NotEqual(Guid.Empty);
}
