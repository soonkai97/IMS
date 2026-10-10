using Ardalis.Specification;
using FluentValidation;
using Ims.Domain;
namespace Ims.Application.Features.Warehouses;

public sealed class WarehouseCodeTakenSpec : Specification<Warehouse>
{
    public WarehouseCodeTakenSpec(string code, int exceptId) => Query.Where(w => w.Code == code && w.Id != exceptId).AsNoTracking();
}
