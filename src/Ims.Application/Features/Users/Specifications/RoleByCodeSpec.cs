using Ardalis.Specification;
using Ims.Domain;
namespace Ims.Application.Features.Users;

/// <summary>The live role with this code (a tiny table: the five system roles and the custom ones), read only, for showing its name.</summary>
public sealed class RoleByCodeSpec : Specification<Role>
{
    public RoleByCodeSpec(string code) => Query.Where(r => r.Code == code).AsNoTracking();
}
