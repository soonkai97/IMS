using Ardalis.Specification;
using Ims.Domain;
namespace Ims.Application.Features.Users;

/// <summary>
/// The live role with this code (a tiny table: the five system roles and the custom ones). <c>tracked</c> when a user is saved with the row: the user then
/// points at the very instance the context holds, not at a second copy of the same key.
/// </summary>
public sealed class RoleByCodeSpec : Specification<Role>
{
    public RoleByCodeSpec(string code, bool tracked = false)
    {
        Query.Where(r => r.Code == code);
        if (!tracked) Query.AsNoTracking();
    }
}
