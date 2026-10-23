using FluentValidation;
using Ims.Domain;
namespace Ims.Application.Features.Sales;

public class CancelSalesOrderValidator : AbstractValidator<CancelSalesOrderCommand>
{
    public CancelSalesOrderValidator() => RuleFor(x => x.Data.Reason).MaximumLength(Limits.RemarkMaxLength);
}
