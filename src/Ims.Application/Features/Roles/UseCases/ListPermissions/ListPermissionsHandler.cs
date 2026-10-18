using Ims.Domain;
namespace Ims.Application.Features.Roles;

/// <summary>The catalog, in the order of the role editor. It is code, so there is nothing to read from the database.</summary>
public class ListPermissionsHandler : IQueryHandler<ListPermissionsQuery, IReadOnlyList<PermissionDto>>
{
    public Task<IReadOnlyList<PermissionDto>> HandleAsync(ListPermissionsQuery q, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<PermissionDto>>(PermissionCatalog.All.Select(p => new PermissionDto(p.Code, p.Group, p.Description, p.IsDangerous, p.AdminCore, p.Requires)).ToList());
}
