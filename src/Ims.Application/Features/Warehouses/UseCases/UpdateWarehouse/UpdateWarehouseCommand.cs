namespace Ims.Application.Features.Warehouses;

public record UpdateWarehouseCommand(int Id, WarehouseSaveRequest Data) : ICommand<WarehouseDto>;
