using FluentValidation;
using Ims.Domain;
namespace Ims.Application.Features.Sales;

public class CloseSalesOrderValidator : AbstractValidator<CloseSalesOrderCommand>
{
    public CloseSalesOrderValidator() => RuleFor(x => x.Data.Reason).MaximumLength(Limits.RemarkMaxLength);
}
