using Ardalis.Specification;
using Ims.Domain;
namespace Ims.Application.Features.Users;

internal static class UserFilters
{
    public static void Apply(ISpecificationBuilder<User> query, UserFilter f)
    {
        // A user is listed whatever the state of their role: the global filter would drop a user whose role row is soft-deleted (the required join), who could then neither be seen nor moved
        // to a live role. So the filters are off here and only the user's own deletion is applied.
        query.AsNoTracking().IgnoreQueryFilters().Where(u => !u.RecDeleted);
        if (!string.IsNullOrWhiteSpace(f.Search))
        {
            var s = f.Search.Trim();
            query.Where(u => u.Username.Contains(s) || u.Email.Contains(s) || u.DisplayName.Contains(s));
        }
        if (!string.IsNullOrWhiteSpace(f.Role)) query.Where(u => u.Role.Code == f.Role);
        if (f.Active is { } a) query.Where(u => u.IsActive == a);
    }
}
