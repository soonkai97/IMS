using FluentValidation;
using Ims.Domain;
namespace Ims.Application.Features.Warehouses;

public class WarehouseSaveRequestValidator : AbstractValidator<WarehouseSaveRequest>
{
    public WarehouseSaveRequestValidator()
    {
        RuleFor(x => x.Code).NotEmpty().WithMessage(Messages.WarehouseCodeRequired).MaximumLength(Limits.CodeMaxLength).WithMessage(Messages.TextTooLong("Warehouse code", Limits.CodeMaxLength));
        RuleFor(x => x.Name).NotEmpty().WithMessage(Messages.WarehouseNameRequired).MaximumLength(Limits.ShortNameMaxLength).WithMessage(Messages.TextTooLong("Warehouse name", Limits.ShortNameMaxLength));
    }
}
