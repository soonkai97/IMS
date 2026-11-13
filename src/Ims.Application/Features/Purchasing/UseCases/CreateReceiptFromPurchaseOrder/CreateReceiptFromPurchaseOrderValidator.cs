using FluentValidation;
namespace Ims.Application.Features.Purchasing;

public sealed class CreateReceiptFromPurchaseOrderValidator : AbstractValidator<CreateReceiptFromPurchaseOrderCommand>
{
    public CreateReceiptFromPurchaseOrderValidator() => RuleFor(x => x.OrderId).NotEqual(Guid.Empty);
}
