namespace Ims.Application.Features.Users;

public record UserDto(int Id, string Username, string Email, string DisplayName, string Role, bool IsActive, IReadOnlyList<int> WarehouseIds);
