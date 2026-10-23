using FluentValidation;
namespace Ims.Application.Features.Sales;

public class CreateSalesOrderValidator : AbstractValidator<CreateSalesOrderCommand>
{
    public CreateSalesOrderValidator() => RuleFor(x => x.Data).SetValidator(new SalesOrderSaveRequestValidator());
}
