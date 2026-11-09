using FluentValidation;
using Ims.Domain;
namespace Ims.Application.Features.Sales;

/// <summary>Shape and limit rules of a price list: every failure is reported with the field it belongs to, all items at once. Rules that need the database (does the product exist, is the customer active, is the name taken) are the handler's.</summary>
public class PriceListSaveRequestValidator : AbstractValidator<PriceListSaveRequest>
{
    public PriceListSaveRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage(Messages.PriceListNameRequired).MaximumLength(Limits.ShortNameMaxLength).WithMessage(Messages.PriceListNameTooLong);
        RuleFor(x => x.CustomerId).NotEqual(Guid.Empty).WithMessage(Messages.ChooseCustomer);
        RuleFor(x => x.Items).NotNull().WithMessage(Messages.RequestNotValid)
            .Must(i => i is null || i.Count <= Limits.MaxPriceListItems).WithMessage(Messages.PriceListTooManyItems(Limits.MaxPriceListItems));
        RuleForEach(x => x.Items).NotNull().WithMessage(Messages.RequestNotValid).ChildRules(item =>
        {
            item.RuleFor(i => i.ProductId).NotEqual(Guid.Empty).WithMessage(Messages.ChooseProduct);
            item.RuleFor(i => i.Price).GreaterThan(0).WithMessage(Messages.PriceListItemPricePositive)
                .LessThanOrEqualTo(Limits.MaxUnitCost).WithMessage(Messages.PriceTooLarge)
                .Must(Money.IsExact).WithMessage(Messages.PriceTooPrecise);
        });
    }
}
