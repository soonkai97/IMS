using FluentValidation;
using Ims.Domain;
namespace Ims.Application.Features.Catalog;

public class ProductSaveRequestValidator : AbstractValidator<ProductSaveRequest>
{
    public ProductSaveRequestValidator()
    {
        RuleFor(x => x.Sku).NotEmpty().MaximumLength(Limits.SkuMaxLength).WithMessage(Messages.SkuRequired);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(Limits.NameMaxLength).WithMessage(Messages.ProductNameRequired);
        RuleFor(x => x.SafetyStock).GreaterThanOrEqualTo(0).WithMessage(Messages.SafetyStockNegative);
        RuleForEach(x => x.Barcodes).NotNull().WithMessage(Messages.BarcodeInvalid).MaximumLength(Limits.BarcodeMaxLength);
        RuleForEach(x => x.Conversions).Must((req, c) => c.Factor > 0 && c.ToUomId == req.BaseUomId)
            .WithMessage(Messages.ConversionInvalid);
    }
}
