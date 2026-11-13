namespace Ims.Application.Features.Warehouses;

public sealed record ListWarehousesQuery(bool IncludeInactive) : IQuery<IReadOnlyList<WarehouseDto>>;
