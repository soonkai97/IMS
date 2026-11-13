using FluentValidation;
namespace Ims.Application.Features.Sales;

public sealed class CreateDeliveryFromSalesOrderValidator : AbstractValidator<CreateDeliveryFromSalesOrderCommand>
{
    public CreateDeliveryFromSalesOrderValidator() => RuleFor(x => x.OrderId).NotEqual(Guid.Empty);
}
