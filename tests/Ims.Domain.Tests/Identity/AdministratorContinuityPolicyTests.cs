namespace Ims.Domain.Tests.Identity;

/// <summary>
/// DDD-15: <see cref="AdministratorContinuityPolicy"/> over the same table that was first asked of the handlers (<see cref="AdministratorContinuityTable"/>, written from the two sentences of the rule), so the
/// policy and the code it replaces give the same answer for every combination. The policy reads nothing and locks nothing: it takes the facts the caller has read, and says whether the other administrators
/// have to be looked for at all (<c>NeedsAnotherAdministrator</c>), so the caller keeps its lock, its query and their order.
/// </summary>
public class AdministratorContinuityPolicyTests
{
    [Fact]
    public void TheUserSave_GivesTheAnswerThatTheTableWrites_ForEveryCombination()
    {
        var wrong = new List<string>();
        foreach (var c in AdministratorContinuityTable.AllUserSaves())
        {
            var answer = AdministratorContinuityPolicy.ForUserSave(c.AdministersNow, c.AdministersAfter, c.Self, c.AnotherAdministrator).ToString();
            if (answer != AdministratorContinuityTable.Expected(c).ToString()) wrong.Add($"{c}: expected {AdministratorContinuityTable.Expected(c)}, got {answer}");

            var needs = AdministratorContinuityPolicy.UserSaveNeedsAnotherAdministrator(c.AdministersNow, c.AdministersAfter, c.Self);
            if (needs != AdministratorContinuityTable.NeedsCount(c)) wrong.Add($"{c}: needs the count {AdministratorContinuityTable.NeedsCount(c)}, got {needs}");
        }

        Assert.True(wrong.Count == 0, $"{wrong.Count} differ:{Environment.NewLine}{string.Join(Environment.NewLine, wrong.Take(15))}");
    }

    [Fact]
    public void TheRolePermissionReplace_GivesTheAnswerThatTheTableWrites_ForEveryCombination()
    {
        var wrong = new List<string>();
        foreach (var c in AdministratorContinuityTable.AllRoleReplaces())
        {
            var answer = AdministratorContinuityPolicy.ForRoleChange(c.Administers, c.WantedHoldsCore, c.AnotherAdministrator).ToString();
            if (answer != AdministratorContinuityTable.Expected(c).ToString()) wrong.Add($"{c}: expected {AdministratorContinuityTable.Expected(c)}, got {answer}");

            var needs = AdministratorContinuityPolicy.RoleChangeNeedsAnotherAdministrator(c.Administers, c.WantedHoldsCore);
            if (needs != AdministratorContinuityTable.NeedsCount(c)) wrong.Add($"{c}: needs the count {AdministratorContinuityTable.NeedsCount(c)}, got {needs}");
        }

        Assert.True(wrong.Count == 0, $"{wrong.Count} differ:{Environment.NewLine}{string.Join(Environment.NewLine, wrong.Take(15))}");
    }

    [Fact]
    public void TheSelfCase_IsDecidedWithoutAskingForAnotherAdministrator()
    {
        // The handler makes no query for a person who locks themselves out: the policy says the count is not needed, and its answer does not depend on it.
        Assert.False(AdministratorContinuityPolicy.UserSaveNeedsAnotherAdministrator(administersNow: true, administersAfter: false, isSelf: true));
        Assert.Equal(AdministratorContinuityRefusal.CannotLockOutSelf, AdministratorContinuityPolicy.ForUserSave(true, false, true, anotherAdministrator: true));
        Assert.Equal(AdministratorContinuityRefusal.CannotLockOutSelf, AdministratorContinuityPolicy.ForUserSave(true, false, true, anotherAdministrator: false));
    }

    [Fact]
    public void TheRefusalsAreTheOnesThatTheTableNames()
    {
        Assert.Equal(["None", "CannotLockOutSelf", "LastAdministrator"], Enum.GetNames<AdministratorContinuityRefusal>());
    }
}
