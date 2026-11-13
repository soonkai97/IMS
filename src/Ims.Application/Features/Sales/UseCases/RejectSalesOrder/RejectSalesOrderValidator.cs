using FluentValidation;
using Ims.Application.Features.Documents;
using Ims.Domain;
namespace Ims.Application.Features.Sales;

public sealed class RejectSalesOrderValidator : AbstractValidator<RejectSalesOrderCommand>
{
    public RejectSalesOrderValidator() => RuleFor(x => x.Data.Reason).MustBeAReason(Messages.RejectReasonRequired);
}
