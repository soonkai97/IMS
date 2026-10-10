namespace Ims.Application.Features.Warehouses;

public record ListWarehousesQuery(bool IncludeInactive) : IQuery<IReadOnlyList<WarehouseDto>>;
