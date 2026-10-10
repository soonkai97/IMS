namespace Ims.Application.Features.Warehouses;

// Warehouses referenced by documents can only be deactivated, never deleted (BR-13): there is deliberately no delete command.
public record CreateWarehouseCommand(WarehouseSaveRequest Data) : ICommand<WarehouseDto>;
