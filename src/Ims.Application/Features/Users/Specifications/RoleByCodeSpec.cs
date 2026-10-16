using Ardalis.Specification;
using Ims.Domain;
namespace Ims.Application.Features.Users;

/// <summary>The live role with this code (a tiny table: the five system roles and the custom ones). A user is saved with the row, so its role id follows its role string.</summary>
public sealed class RoleByCodeSpec : Specification<Role>
{
    public RoleByCodeSpec(string code) => Query.Where(r => r.Code == code).AsNoTracking();
}
