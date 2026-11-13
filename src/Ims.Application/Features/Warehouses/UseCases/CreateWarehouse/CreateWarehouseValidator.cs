using FluentValidation;
namespace Ims.Application.Features.Warehouses;

public sealed class CreateWarehouseValidator : AbstractValidator<CreateWarehouseCommand>
{
    public CreateWarehouseValidator() => RuleFor(x => x.Data).SetValidator(new WarehouseSaveRequestValidator());
}
