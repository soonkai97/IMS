namespace Ims.Application.Features.Auth;

public record UserInfo(int Id, string Username, string DisplayName, string Role, IReadOnlyList<string> Permissions, IReadOnlyList<Guid> WarehouseIds);
