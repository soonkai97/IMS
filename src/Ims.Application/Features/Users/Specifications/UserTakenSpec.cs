using Ardalis.Specification;
using FluentValidation;
using Ims.Domain;
namespace Ims.Application.Features.Users;

public sealed class UserTakenSpec : Specification<User>
{
    public UserTakenSpec(string username, string email, int exceptId = 0) =>
        Query.Where(u => u.Id != exceptId && (u.Username == username || u.Email == email)).AsNoTracking();
}
