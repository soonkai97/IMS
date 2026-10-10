using Ardalis.Specification;
using Ims.Domain;
namespace Ims.Application.Features.Users;

internal static class UserFilters
{
    public static void Apply(ISpecificationBuilder<User> query, UserFilter f)
    {
        query.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(f.Search))
        {
            var s = f.Search.Trim();
            query.Where(u => u.Username.Contains(s) || u.Email.Contains(s) || u.DisplayName.Contains(s));
        }
        if (!string.IsNullOrWhiteSpace(f.Role)) query.Where(u => u.Role == f.Role);
        if (f.Active is { } a) query.Where(u => u.IsActive == a);
    }
}
