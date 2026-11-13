namespace Ims.Application.Features.Warehouses;

public sealed record UpdateWarehouseCommand(Guid Id, WarehouseSaveRequest Data) : ICommand<WarehouseDto>;
