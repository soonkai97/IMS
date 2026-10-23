using Ardalis.Specification;
using Ims.Domain;
namespace Ims.Application.Features.Sales;

/// <summary>The few people named on an order (the submitter, the approver, the reviewer who sent it back), by integer key, in one bounded query.</summary>
public sealed class UsersByKeysSpec : Specification<User, UserLabel>
{
    public UsersByKeysSpec(IReadOnlyCollection<int> keys) => Query.AsNoTracking().Where(u => keys.Contains(u.Id)).Select(u => new UserLabel(u.Id, u.PublicId, u.Username, u.DisplayName));
}
