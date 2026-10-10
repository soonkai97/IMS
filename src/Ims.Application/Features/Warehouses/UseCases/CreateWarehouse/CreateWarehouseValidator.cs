using FluentValidation;
namespace Ims.Application.Features.Warehouses;

public class CreateWarehouseValidator : AbstractValidator<CreateWarehouseCommand>
{
    public CreateWarehouseValidator() => RuleFor(x => x.Data).SetValidator(new WarehouseSaveRequestValidator());
}
