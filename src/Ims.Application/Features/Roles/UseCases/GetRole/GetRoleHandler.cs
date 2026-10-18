using Ims.Domain;
namespace Ims.Application.Features.Roles;

public class GetRoleHandler(IRoleReader roles) : IQueryHandler<GetRoleQuery, RoleDto>
{
    public async Task<RoleDto> HandleAsync(GetRoleQuery q, CancellationToken ct) => await roles.FindAsync(q.Id, ct) ?? throw new NotFoundException(Messages.RoleNotFound);
}
