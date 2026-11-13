using FluentValidation;
using Ims.Domain;
namespace Ims.Application.Features.Purchasing;

public sealed class ClosePurchaseOrderValidator : AbstractValidator<ClosePurchaseOrderCommand>
{
    public ClosePurchaseOrderValidator() => RuleFor(x => x.Data.Reason).MaximumLength(Limits.RemarkMaxLength);
}
