using FluentValidation;
using Ims.Domain;
namespace Ims.Application.Features.Warehouses;

public class WarehouseSaveRequestValidator : AbstractValidator<WarehouseSaveRequest>
{
    public WarehouseSaveRequestValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(Limits.CodeMaxLength).WithMessage(Messages.WarehouseCodeRequired);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(Limits.ShortNameMaxLength).WithMessage(Messages.WarehouseNameRequired);
    }
}
