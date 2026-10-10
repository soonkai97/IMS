using Ardalis.Specification;
using FluentValidation;
using Ims.Domain;
namespace Ims.Application.Features.Warehouses;

public sealed class WarehousesSpec : Specification<Warehouse>
{
    public WarehousesSpec(bool includeInactive)
    {
        Query.AsNoTracking().OrderBy(w => w.Code);
        if (!includeInactive) Query.Where(w => w.IsActive);
    }
}
