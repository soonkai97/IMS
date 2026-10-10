using Ardalis.Specification;
using Ims.Domain;
namespace Ims.Application.Features.Attachments;

public sealed class UsernamesByIdsSpec : Specification<User, UserName>
{
    public UsernamesByIdsSpec(IReadOnlyCollection<int> ids) =>
        Query.AsNoTracking().Where(u => ids.Contains(u.Id)).Select(u => new UserName(u.Id, u.Username));
}
