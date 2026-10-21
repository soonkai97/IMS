using FluentValidation;
using Ims.Application.Features.Documents;
using Ims.Domain;
namespace Ims.Application.Features.Purchasing;

public class RejectPurchaseOrderValidator : AbstractValidator<RejectPurchaseOrderCommand>
{
    public RejectPurchaseOrderValidator() => RuleFor(x => x.Data.Reason).MustBeAReason(Messages.RejectReasonRequired);
}
