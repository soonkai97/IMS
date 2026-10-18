namespace Ims.Domain;

/// <summary>A change a system role does not allow (rename, deactivate, delete, or a new permission set for the Administrator role). The API answers 409, like a role that is in use.</summary>
public class LockedRoleException(string message) : DomainException(message);
