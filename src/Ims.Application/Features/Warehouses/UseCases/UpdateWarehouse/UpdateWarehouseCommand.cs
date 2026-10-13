namespace Ims.Application.Features.Warehouses;

public record UpdateWarehouseCommand(Guid Id, WarehouseSaveRequest Data) : ICommand<WarehouseDto>;
