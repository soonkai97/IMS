namespace Ims.Domain.Tests.Identity;

/// <summary>What a write that could take an administrator away comes to. The member names are the ones of the policy's refusal enum, so a policy answer compares by name.</summary>
public enum ContinuityOutcome { None, CannotLockOutSelf, LastAdministrator }

/// <summary>
/// DDD-15: administrator continuity, over every combination of facts. First asked of the user save and the role permission replace as they are on <c>main</c> (characterization), then of
/// <c>AdministratorContinuityPolicy</c>: one table, so both give the same answers. The expected outcome is written out here from the two sentences of the rule (ADR roles-permissions 2.9), not copied from the code:
/// (1) a person who administers (active, and their role is live and holds the whole administration core) is not left without administering, by deactivating them or giving them a role that does not, by themselves
/// (the sentence "You cannot deactivate your own account or remove your own administrator role.") and (2) nor by someone else when no other active administrator would remain ("At least one active administrator must
/// remain."). A role that administers does not lose a core permission when no other administrator would remain; there is no self case there (the caller cannot edit their own role).
/// </summary>
public static class AdministratorContinuityTable
{
    /// <summary>A user save: the person edited, what they are given, whether the caller is that person, and whether an administrator other than them exists.</summary>
    public sealed record UserSave(bool Active, bool RoleAdministers, bool NewActive, bool NewRoleAdministers, bool Self, bool AnotherAdministrator)
    {
        public bool AdministersNow => Active && RoleAdministers;
        public bool AdministersAfter => NewActive && NewRoleAdministers;

        public override string ToString() =>
            $"{(Self ? "self" : "other")}: {(Active ? "active" : "inactive")} {(RoleAdministers ? "administrator" : "plain")} role -> {(NewActive ? "active" : "inactive")} {(NewRoleAdministers ? "administrator" : "plain")} role, {(AnotherAdministrator ? "another" : "no other")} administrator";
    }

    /// <summary>A permission replace: whether the role administers now (live, holds the core), whether the new set holds the core, and whether an administrator exists who does not hold this role.</summary>
    public sealed record RoleReplace(bool Administers, bool WantedHoldsCore, bool AnotherAdministrator)
    {
        public override string ToString() => $"{(Administers ? "administering" : "plain")} role -> {(WantedHoldsCore ? "with" : "without")} the core, {(AnotherAdministrator ? "another" : "no other")} administrator";
    }

    public static ContinuityOutcome Expected(UserSave c)
    {
        if (!c.AdministersNow || c.AdministersAfter) return ContinuityOutcome.None;
        if (c.Self) return ContinuityOutcome.CannotLockOutSelf;
        return c.AnotherAdministrator ? ContinuityOutcome.None : ContinuityOutcome.LastAdministrator;
    }

    public static ContinuityOutcome Expected(RoleReplace c) =>
        c.Administers && !c.WantedHoldsCore && !c.AnotherAdministrator ? ContinuityOutcome.LastAdministrator : ContinuityOutcome.None;

    /// <summary>Whether the answer needs the count of the other administrators (and so a query): only a lock-out that is not the caller's own.</summary>
    public static bool NeedsCount(UserSave c) => c.AdministersNow && !c.AdministersAfter && !c.Self;

    public static bool NeedsCount(RoleReplace c) => c.Administers && !c.WantedHoldsCore;

    public static IEnumerable<UserSave> AllUserSaves() =>
        from active in new[] { true, false }
        from roleAdministers in new[] { true, false }
        from newActive in new[] { true, false }
        from newRoleAdministers in new[] { true, false }
        from self in new[] { true, false }
        from another in new[] { true, false }
        select new UserSave(active, roleAdministers, newActive, newRoleAdministers, self, another);

    public static IEnumerable<RoleReplace> AllRoleReplaces() =>
        from administers in new[] { true, false }
        from wantedCore in new[] { true, false }
        from another in new[] { true, false }
        select new RoleReplace(administers, wantedCore, another);
}
