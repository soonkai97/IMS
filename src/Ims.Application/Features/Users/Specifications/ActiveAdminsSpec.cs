using Ardalis.Specification;
using Ims.Domain;
namespace Ims.Application.Features.Users;

/// <summary>Active administrators other than the given user: used to refuse removing the last one.</summary>
public sealed class ActiveAdminsSpec : Specification<User>
{
    public ActiveAdminsSpec(int exceptUserId) =>
        Query.AsNoTracking().Where(u => u.Role == Roles.Admin && u.IsActive && u.Id != exceptUserId);
}
