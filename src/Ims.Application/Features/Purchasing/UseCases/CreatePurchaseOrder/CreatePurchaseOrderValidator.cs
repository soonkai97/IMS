using FluentValidation;
namespace Ims.Application.Features.Purchasing;

public sealed class CreatePurchaseOrderValidator : AbstractValidator<CreatePurchaseOrderCommand>
{
    public CreatePurchaseOrderValidator() => RuleFor(x => x.Data).SetValidator(new PurchaseOrderSaveRequestValidator());
}
