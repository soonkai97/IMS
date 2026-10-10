using Ardalis.Specification;
using FluentValidation;
using Ims.Domain;
namespace Ims.Application.Features.Import;

public sealed class ActiveWarehousesSpec : Specification<Warehouse>
{
    public ActiveWarehousesSpec() => Query.AsNoTracking().Where(w => w.IsActive);
}
