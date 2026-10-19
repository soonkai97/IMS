namespace Ims.Application.Features.Users;

/// <summary>A role the caller may give to a user: the code the user is saved with and the name people read. No permission list: a user manager need not see what a role holds.</summary>
public record AssignableRoleDto(string Code, string Name);
