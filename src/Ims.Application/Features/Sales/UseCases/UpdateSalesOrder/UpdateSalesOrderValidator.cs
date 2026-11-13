using FluentValidation;
namespace Ims.Application.Features.Sales;

public sealed class UpdateSalesOrderValidator : AbstractValidator<UpdateSalesOrderCommand>
{
    public UpdateSalesOrderValidator() => RuleFor(x => x.Data).SetValidator(new SalesOrderSaveRequestValidator());
}
