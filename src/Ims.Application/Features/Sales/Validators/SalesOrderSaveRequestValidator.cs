using FluentValidation;
using Ims.Domain;
namespace Ims.Application.Features.Sales;

/// <summary>Shape and limit rules of a draft: every failure is reported with the field it belongs to, all lines at once. Rules that need the database (does the product exist, is the customer active) are the handler's.</summary>
public class SalesOrderSaveRequestValidator : AbstractValidator<SalesOrderSaveRequest>
{
    public SalesOrderSaveRequestValidator()
    {
        RuleFor(x => x.WarehouseId).NotEqual(Guid.Empty).WithMessage(Messages.ChooseWarehouse);
        RuleFor(x => x.CustomerId).NotEqual(Guid.Empty).WithMessage(Messages.ChooseCustomer);
        RuleFor(x => x.Remark).MaximumLength(Limits.RemarkMaxLength);
        RuleFor(x => x.Lines).NotEmpty().WithMessage(Messages.AddLine)
            .Must(l => l is null || l.Count <= Limits.MaxDocumentLines).WithMessage(Messages.TooManyLines(Limits.MaxDocumentLines));
        RuleForEach(x => x.Lines).NotNull().WithMessage(Messages.RequestNotValid).ChildRules(line =>
        {
            line.RuleFor(l => l.ProductId).NotEqual(Guid.Empty).WithMessage(Messages.ChooseProduct);
            line.RuleFor(l => l.Qty).GreaterThan(0).WithMessage(Messages.QtyPositive)
                .LessThanOrEqualTo(Limits.MaxQuantity).WithMessage(Messages.QtyTooLarge)
                .PrecisionScale(Limits.QuantityPrecision, Limits.QuantityScale, ignoreTrailingZeros: true).WithMessage(Messages.QtyTooLarge);
            line.RuleFor(l => l.UnitPrice).GreaterThanOrEqualTo(0).WithMessage(Messages.PriceNegative)
                .LessThanOrEqualTo(Limits.MaxUnitCost).WithMessage(Messages.PriceTooLarge)
                .Must(Money.IsExact).WithMessage(Messages.PriceTooPrecise)
                .Must((l, price) => AmountFits(l.Qty, price)).WithMessage(Messages.LineAmountTooLarge);
            line.RuleFor(l => l.Remark).MaximumLength(Limits.RemarkMaxLength);
        });
    }

    /// <summary>The amount of a line is entered quantity x entered price and must fit the column, whatever unit the quantity is in. A value another rule already refuses is not judged twice.</summary>
    private static bool AmountFits(decimal qty, decimal price) =>
        qty <= 0 || qty > Limits.MaxQuantity || price < 0 || price > Limits.MaxUnitCost || Money.Amount(qty, price) <= Limits.MaxLineAmount;
}
