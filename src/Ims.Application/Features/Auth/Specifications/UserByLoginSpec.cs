using Ardalis.Specification;
using FluentValidation;
using Ims.Domain;
namespace Ims.Application.Features.Auth;

public sealed class UserByLoginSpec : Specification<User>
{
    public UserByLoginSpec(string loginKey) =>
        Query.Where(u => u.Username.ToLower() == loginKey || u.Email.ToLower() == loginKey).Include(u => u.Warehouses);
}
