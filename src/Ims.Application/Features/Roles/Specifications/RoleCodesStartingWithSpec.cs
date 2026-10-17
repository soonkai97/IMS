using Ardalis.Specification;
using Ims.Domain;
namespace Ims.Application.Features.Roles;

/// <summary>The codes of the live roles that start with the given text: what a new code must not collide with.</summary>
public sealed class RoleCodesStartingWithSpec : Specification<Role, string>
{
    public RoleCodesStartingWithSpec(string prefix) => Query.AsNoTracking().Where(r => r.Code.StartsWith(prefix)).Select(r => r.Code);
}
