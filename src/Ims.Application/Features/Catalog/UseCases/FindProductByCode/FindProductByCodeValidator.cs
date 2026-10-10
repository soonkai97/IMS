using FluentValidation;
using Ims.Domain;
namespace Ims.Application.Features.Catalog;

public class FindProductByCodeValidator : AbstractValidator<FindProductByCodeQuery>
{
    /// <summary>No SKU or barcode is longer than the longer of the two limits, so a longer code cannot match and never needs to reach the database.</summary>
    public FindProductByCodeValidator() => RuleFor(x => x.Code).MaximumLength(Limits.BarcodeMaxLength).WithMessage(Messages.SearchTooLong);
}
