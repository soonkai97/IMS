namespace Ims.Application.Features.Users;

public record UserDto(Guid Id, string Username, string Email, string DisplayName, string Role, string RoleName, bool IsActive, IReadOnlyList<Guid> WarehouseIds, bool AllWarehouses);
