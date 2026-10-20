using FluentValidation;
namespace Ims.Application.Features.Purchasing;

public class UpdatePurchaseOrderValidator : AbstractValidator<UpdatePurchaseOrderCommand>
{
    public UpdatePurchaseOrderValidator() => RuleFor(x => x.Data).SetValidator(new PurchaseOrderSaveRequestValidator());
}
