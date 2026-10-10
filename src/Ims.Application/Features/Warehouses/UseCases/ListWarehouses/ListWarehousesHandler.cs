using Ardalis.Specification;
using FluentValidation;
using Ims.Domain;
namespace Ims.Application.Features.Warehouses;

/// <summary>Only warehouses the caller may access are returned (AUTH-02 data scope).</summary>
public class ListWarehousesHandler(IReadRepository<Warehouse> warehouses, ICurrentUser current)
    : IQueryHandler<ListWarehousesQuery, IReadOnlyList<WarehouseDto>>
{
    public async Task<IReadOnlyList<WarehouseDto>> HandleAsync(ListWarehousesQuery q, CancellationToken ct) =>
        (await warehouses.ListAsync(new WarehousesSpec(q.IncludeInactive), ct))
        .Where(w => current.CanAccessWarehouse(w.Id))
        .Select(w => new WarehouseDto(w.Id, w.Code, w.Name, w.Contact, w.Notes, w.IsActive)).ToList();
}
