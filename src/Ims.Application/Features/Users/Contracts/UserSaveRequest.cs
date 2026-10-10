namespace Ims.Application.Features.Users;

public record UserSaveRequest(string Username, string Email, string DisplayName, string Role, bool IsActive, string? Password, IReadOnlyList<int>? WarehouseIds);
