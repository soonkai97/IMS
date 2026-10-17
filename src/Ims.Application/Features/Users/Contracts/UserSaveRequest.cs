namespace Ims.Application.Features.Users;

/// <param name="Role">The code of a role in the Role table (a system role or a custom one).</param>
/// <param name="AllWarehouses">Sees every warehouse whatever <paramref name="WarehouseIds"/> says. Left out: unchanged on an update, false for a new user.</param>
public record UserSaveRequest(string Username, string Email, string DisplayName, string Role, bool IsActive, string? Password, IReadOnlyList<Guid>? WarehouseIds, bool? AllWarehouses = null);
