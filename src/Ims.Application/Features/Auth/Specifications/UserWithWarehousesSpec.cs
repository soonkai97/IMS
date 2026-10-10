using Ardalis.Specification;
using FluentValidation;
using Ims.Domain;
namespace Ims.Application.Features.Auth;

public sealed class UserWithWarehousesSpec : Specification<User>
{
    public UserWithWarehousesSpec(int id, bool tracked = true)
    {
        Query.Where(u => u.Id == id).Include(u => u.Warehouses);
        if (!tracked) Query.AsNoTracking();
    }
}
