using FluentValidation;
using Ims.Application.Features.Modules;
using Ims.Domain;
namespace Ims.Application.Features.Catalog;

public class ProductSaveRequestValidator : AbstractValidator<ProductSaveRequest>
{
    public ProductSaveRequestValidator()
    {
        RuleFor(x => x.Sku).NotEmpty().WithMessage(Messages.SkuRequired).MaximumLength(Limits.SkuMaxLength).WithMessage(Messages.TextTooLong("SKU", Limits.SkuMaxLength));
        RuleFor(x => x.Name).NotEmpty().WithMessage(Messages.ProductNameRequired).MaximumLength(Limits.NameMaxLength).WithMessage(Messages.TextTooLong("Product name", Limits.NameMaxLength));
        RuleFor(x => x.HsCode.Value).HasTaxFieldShape(nameof(ProductSaveRequest.HsCode), TaxFieldShapes.HsCodeError);
        RuleFor(x => x.SstCode.Value).HasTaxFieldShape(nameof(ProductSaveRequest.SstCode), TaxFieldShapes.SstCodeError);
        RuleFor(x => x.SafetyStock).GreaterThanOrEqualTo(0).WithMessage(Messages.SafetyStockNegative);
        RuleForEach(x => x.Barcodes).NotNull().WithMessage(Messages.BarcodeInvalid).MaximumLength(Limits.BarcodeMaxLength);
        RuleForEach(x => x.Conversions).Must((req, c) => c.Factor > 0 && c.ToUomId == req.BaseUomId)
            .WithMessage(Messages.ConversionInvalid);
    }
}
