namespace Ims.Domain;

/// <summary>Why a write that could take an administrator away is refused; <see cref="None"/> when it may go on. The caller maps each member to its sentence and status.</summary>
public enum AdministratorContinuityRefusal
{
    None,

    /// <summary>The person would deactivate their own account or give themselves a role that does not administer.</summary>
    CannotLockOutSelf,

    /// <summary>No other active administrator would remain.</summary>
    LastAdministrator
}
