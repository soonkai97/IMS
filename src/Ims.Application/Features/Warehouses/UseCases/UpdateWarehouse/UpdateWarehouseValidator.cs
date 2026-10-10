using FluentValidation;
namespace Ims.Application.Features.Warehouses;

public class UpdateWarehouseValidator : AbstractValidator<UpdateWarehouseCommand>
{
    public UpdateWarehouseValidator() => RuleFor(x => x.Data).SetValidator(new WarehouseSaveRequestValidator());
}
