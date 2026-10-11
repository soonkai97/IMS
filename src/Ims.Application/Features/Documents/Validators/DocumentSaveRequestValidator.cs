using FluentValidation;
using Ims.Domain;
namespace Ims.Application.Features.Documents;

public class DocumentSaveRequestValidator : AbstractValidator<DocumentSaveRequest>
{
    public DocumentSaveRequestValidator()
    {
        RuleFor(x => x.WarehouseId).GreaterThan(0).WithMessage(Messages.ChooseWarehouse);
        RuleFor(x => x.Remark).MaximumLength(Limits.RemarkMaxLength);
        RuleFor(x => x.Lines).NotEmpty().WithMessage(Messages.AddLine)
            .Must(l => l is null || l.Count <= Limits.MaxDocumentLines).WithMessage(Messages.TooManyLines(Limits.MaxDocumentLines));
        RuleFor(x => x.Purpose).IsInEnum().When(x => x.Purpose is not null);
        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.ProductId).GreaterThan(0).WithMessage(Messages.ChooseProduct);
            line.RuleFor(l => l.Qty).GreaterThan(0).WithMessage(Messages.QtyPositive)
                .LessThanOrEqualTo(Limits.MaxQuantity).WithMessage(Messages.QtyTooLarge)
                .PrecisionScale(Limits.QuantityPrecision, Limits.QuantityScale, ignoreTrailingZeros: true).WithMessage(Messages.QtyTooLarge);
            line.RuleFor(l => l.UnitCost).LessThanOrEqualTo(Limits.MaxUnitCost).When(l => l.UnitCost is not null).WithMessage(Messages.CostTooLarge);
            line.RuleFor(l => l.UnitCost).GreaterThanOrEqualTo(0).When(l => l.UnitCost is not null).WithMessage(Messages.CostNegative);
            line.RuleFor(l => l.Remark).MaximumLength(Limits.RemarkMaxLength);
        });
    }
}
