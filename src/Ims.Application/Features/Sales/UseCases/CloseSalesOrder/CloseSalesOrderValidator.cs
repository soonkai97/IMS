using FluentValidation;
using Ims.Domain;
namespace Ims.Application.Features.Sales;

public sealed class CloseSalesOrderValidator : AbstractValidator<CloseSalesOrderCommand>
{
    public CloseSalesOrderValidator() => RuleFor(x => x.Data.Reason).MaximumLength(Limits.RemarkMaxLength);
}
