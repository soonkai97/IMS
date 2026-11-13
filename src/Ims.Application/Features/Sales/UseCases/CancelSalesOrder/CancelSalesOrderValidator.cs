using FluentValidation;
using Ims.Domain;
namespace Ims.Application.Features.Sales;

public sealed class CancelSalesOrderValidator : AbstractValidator<CancelSalesOrderCommand>
{
    public CancelSalesOrderValidator() => RuleFor(x => x.Data.Reason).MaximumLength(Limits.RemarkMaxLength);
}
