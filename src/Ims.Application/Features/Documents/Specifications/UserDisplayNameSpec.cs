using Ardalis.Specification;
using Ims.Domain;
namespace Ims.Application.Features.Documents;

public sealed class UserDisplayNameSpec : Specification<User, string>
{
    public UserDisplayNameSpec(int userId) => Query.AsNoTracking().Where(u => u.Id == userId).Select(u => u.DisplayName);
}
