namespace Ims.Application.Features.Roles;

public record RoleFilter(string? Search, bool IncludeInactive = false);
