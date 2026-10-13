namespace Ims.Application.Features.Auth;

public record UserInfo(Guid Id, string Username, string DisplayName, string Role, IReadOnlyList<string> Permissions, IReadOnlyList<Guid> WarehouseIds);
