namespace Ims.Domain;

/// <summary>
/// The system always keeps at least one active administrator (ADR roles-permissions 2.9): a person who is active and whose role is live and holds the whole administration core (<see cref="Role.Administers"/>),
/// whatever the role is called. Two writes could take the last one away, the user save and the role permission replace, and a person must not lock themselves out. The policy takes the facts the caller has read (does
/// the target administer now, would they after the write, is the target the caller, does another administrator remain) and answers. It reads nothing and locks nothing: the handler takes the <c>roles</c> lock before
/// it reads anything, and makes the query for the other administrators, in that order, only when the <c>...NeedsAnotherAdministrator</c> question says the answer depends on it, so a write that cannot take anyone
/// away makes no query. A role that is in use is not deactivated or deleted (a different refusal, "role in use"), so those two writes need no rule here.
/// </summary>
public static class AdministratorContinuityPolicy
{
    /// <summary>
    /// Whether a user save is refused, and why. A person who administers is not left without administering (deactivated, or given a role that does not administer): by themselves that is a lock-out of their own
    /// (<see cref="AdministratorContinuityRefusal.CannotLockOutSelf"/>, decided without asking for anyone else); by another it is refused when no other administrator would remain.
    /// <paramref name="anotherAdministrator"/> is looked at only when <see cref="UserSaveNeedsAnotherAdministrator"/> is true; pass anything otherwise.
    /// </summary>
    public static AdministratorContinuityRefusal ForUserSave(bool administersNow, bool administersAfter, bool isSelf, bool anotherAdministrator)
    {
        if (!administersNow || administersAfter) return AdministratorContinuityRefusal.None;
        if (isSelf) return AdministratorContinuityRefusal.CannotLockOutSelf;
        return anotherAdministrator ? AdministratorContinuityRefusal.None : AdministratorContinuityRefusal.LastAdministrator;
    }

    /// <summary>Whether the other administrators have to be looked for to answer <see cref="ForUserSave"/>: an administrator who would stop administering, and is not the caller.</summary>
    public static bool UserSaveNeedsAnotherAdministrator(bool administersNow, bool administersAfter, bool isSelf) => administersNow && !administersAfter && !isSelf;

    /// <summary>
    /// Whether a change of the permissions of a role is refused: a role that administers now cannot lose any of the core when no administrator would remain without it. <paramref name="anotherAdministrator"/> counts the
    /// active administrators who do not hold this role, and is looked at only when <see cref="RoleChangeNeedsAnotherAdministrator"/> is true. There is no self case: nobody edits the role they hold.
    /// </summary>
    public static AdministratorContinuityRefusal ForRoleChange(bool administersNow, bool holdsAdminCoreAfter, bool anotherAdministrator) =>
        RoleChangeNeedsAnotherAdministrator(administersNow, holdsAdminCoreAfter) && !anotherAdministrator ? AdministratorContinuityRefusal.LastAdministrator : AdministratorContinuityRefusal.None;

    /// <summary>Whether the other administrators have to be looked for to answer <see cref="ForRoleChange"/>: a role that administers now and would no longer hold the whole core.</summary>
    public static bool RoleChangeNeedsAnotherAdministrator(bool administersNow, bool holdsAdminCoreAfter) => administersNow && !holdsAdminCoreAfter;
}
