using FluentValidation;
namespace Ims.Application.Features.Sales;

public sealed class CreateSalesOrderValidator : AbstractValidator<CreateSalesOrderCommand>
{
    public CreateSalesOrderValidator() => RuleFor(x => x.Data).SetValidator(new SalesOrderSaveRequestValidator());
}
