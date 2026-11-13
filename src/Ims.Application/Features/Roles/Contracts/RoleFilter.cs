namespace Ims.Application.Features.Roles;

public sealed record RoleFilter(string? Search, bool IncludeInactive = false);
