using Ims.Application;
using Ims.Application.Features.Roles;
using Ims.Application.Features.Users;
using Ims.Domain.Tests.Stock;
using static Ims.Domain.Tests.Identity.IdentityHandlerFakes;
using static Ims.Domain.Tests.TestData;

namespace Ims.Domain.Tests.Identity;

/// <summary>
/// DDD-15, characterization: <c>UpdateUserHandler</c> and <c>ReplaceRolePermissionsHandler</c> as they are on <c>main</c>, before the continuity decision moves into <c>AdministratorContinuityPolicy</c>.
/// Green before the change and green after it: the same exception, status class and sentence for every combination of <see cref="AdministratorContinuityTable"/>, the same order of the roles lock, the count of the
/// other administrators and the save, and no count when the answer does not need one. Real specifications run over the rows, so "active, live role, whole core, not the person or the role being changed" is the
/// real filter, and every world holds decoys that must not count (an inactive administrator, a plain user, a person whose administrator role is inactive). <c>Ims.Api.Tests</c> (<c>AdministrabilityTests</c>,
/// <c>LastAdminTests</c>, <c>SessionSecurityTests</c>) prove the status on the wire and the race between two writes.
/// </summary>
public class AdministratorContinuityHandlerTests
{
    private const int TargetId = 10, OtherCallerId = 20, AnotherAdministratorId = 30;

    private static Role AdministeringRole(int id = 5) => Role.Create($"Deputy{id}", $"Deputy {id}", null, false, PermissionCatalog.AdminCoreCodes).WithId(id);

    private static Role PlainRole(int id = 6) => Role.Create($"Plain{id}", $"Plain {id}", null, false, [Permissions.MasterDataRead, Permissions.StockRead]).WithId(id);

    private static User UserOf(int id, Role role, bool active = true) =>
        User.Create($"user{id}", $"user{id}@example.test", $"User {id}", role, "hash", isActive: active).WithId(id);

    /// <summary>People who must not count as another administrator: an inactive one, one without the core, one whose administrator role is inactive.</summary>
    private static IEnumerable<User> Decoys(Role administering)
    {
        var inactiveRole = AdministeringRole(id: 9);
        var ofInactiveRole = UserOf(33, inactiveRole);
        inactiveRole.SetActive(false);   // a user cannot be given an inactive role, but a role can be deactivated under its holders by a manual edit
        return [UserOf(31, administering, active: false), UserOf(32, PlainRole(), active: true), ofInactiveRole];
    }

    private static ContinuityOutcome OutcomeOf(Exception? e) => e switch
    {
        null => ContinuityOutcome.None,
        ConflictException { Message: Messages.CannotLockOutSelf } => ContinuityOutcome.CannotLockOutSelf,
        ConflictException { Message: Messages.LastAdminRequired } => ContinuityOutcome.LastAdministrator,
        _ => throw new InvalidOperationException($"Unexpected {e.GetType().Name}: {e.Message}")
    };

    [Fact]
    public async Task TheUserSave_GivesTheAnswerThatTheTableWrites_ForEveryCombination()
    {
        var wrong = new List<string>();
        var count = 0;
        foreach (var c in AdministratorContinuityTable.AllUserSaves())
        {
            count++;
            var journal = new Journal();
            var admin = AdministeringRole();
            var plain = PlainRole();
            var target = UserOf(TargetId, c.RoleAdministers ? admin : plain, c.Active);
            var users = new List<User> { target };
            users.AddRange(Decoys(admin));
            if (c.AnotherAdministrator) users.Add(UserOf(AnotherAdministratorId, admin));
            var handler = Build<UpdateUserHandler>(new IdentityStore<User>(users, journal), new IdentityStore<Role>([admin, plain], journal), new IdentityStore<RefreshToken>([], journal), new NoWarehouses(), new FakeHasher(),
                new FullAccessCaller(c.Self ? TargetId : OtherCallerId), new FakeClock(), new IdentityLocker(journal), new IdentityWork(journal), new NoAudit());
            var request = new UserSaveRequest(target.Username, target.Email, target.DisplayName, (c.NewRoleAdministers ? admin : plain).Code, c.NewActive, null, []);

            Exception? thrown = null;
            try { await handler.HandleAsync(new UpdateUserCommand(target.PublicId, request), CancellationToken.None); }
            catch (ConflictException e) { thrown = e; }

            var expected = AdministratorContinuityTable.Expected(c);
            if (OutcomeOf(thrown) != expected) wrong.Add($"{c}: expected {expected}, got {OutcomeOf(thrown)}");

            List<string> steps = [LockRoles];
            if (AdministratorContinuityTable.NeedsCount(c)) steps.Add(CountAdministrators);
            if (expected == ContinuityOutcome.None)
            {
                if (c.Active && !c.NewActive) steps.Add(ListTokens);
                steps.Add(Save);
            }
            if (!steps.SequenceEqual(journal)) wrong.Add($"{c}: expected steps [{string.Join(", ", steps)}], got [{string.Join(", ", journal)}]");

            var (role, active) = expected == ContinuityOutcome.None ? ((c.NewRoleAdministers ? admin : plain).Code, c.NewActive) : ((c.RoleAdministers ? admin : plain).Code, c.Active);
            if ((target.Role.Code, target.IsActive) != (role, active)) wrong.Add($"{c}: the user ended as {target.Role.Code}/{target.IsActive}");
        }

        Assert.Equal(64, count);
        Assert.Equal(Enum.GetValues<ContinuityOutcome>().Length, AdministratorContinuityTable.AllUserSaves().Select(AdministratorContinuityTable.Expected).Distinct().Count()); // the grid reaches every outcome
        Assert.True(wrong.Count == 0, $"{wrong.Count} of {count} differ:{Environment.NewLine}{string.Join(Environment.NewLine, wrong.Take(15))}");
    }

    [Fact]
    public async Task TheRolePermissionReplace_GivesTheAnswerThatTheTableWrites_ForEveryCombination()
    {
        var wrong = new List<string>();
        var count = 0;
        foreach (var c in AdministratorContinuityTable.AllRoleReplaces())
        {
            count++;
            var journal = new Journal();
            var role = c.Administers ? AdministeringRole(id: 4) : PlainRole(id: 4);
            var other = AdministeringRole();
            // The holder of the role is an administrator when the role administers, and must not count as another: the count leaves the holders of the role out.
            var users = new List<User> { UserOf(40, role) };
            users.AddRange(Decoys(other));
            if (c.AnotherAdministrator) users.Add(UserOf(AnotherAdministratorId, other));
            var handler = Build<ReplaceRolePermissionsHandler>(new IdentityStore<Role>([role], journal), new IdentityStore<User>(users, journal), new StubRoleReader(), new AnyVersion(), new FullAccessCaller(99),
                new IdentityLocker(journal), new EverythingOffered(), new IdentityWork(journal), new NoAudit());
            string[] wanted = c.WantedHoldsCore ? [.. PermissionCatalog.AdminCoreCodes, Permissions.MasterDataRead] : [Permissions.MasterDataRead];

            Exception? thrown = null;
            try { await handler.HandleAsync(new ReplaceRolePermissionsCommand(role.PublicId, new RolePermissionsRequest(wanted), null), CancellationToken.None); }
            catch (ConflictException e) { thrown = e; }

            var expected = AdministratorContinuityTable.Expected(c);
            if (OutcomeOf(thrown) != expected) wrong.Add($"{c}: expected {expected}, got {OutcomeOf(thrown)}");

            List<string> steps = [LockRoles];
            if (AdministratorContinuityTable.NeedsCount(c)) steps.Add(CountAdministrators);
            if (expected == ContinuityOutcome.None) steps.Add(Save);
            if (!steps.SequenceEqual(journal)) wrong.Add($"{c}: expected steps [{string.Join(", ", steps)}], got [{string.Join(", ", journal)}]");
        }

        Assert.Equal(8, count);
        Assert.Equal(2, AdministratorContinuityTable.AllRoleReplaces().Select(AdministratorContinuityTable.Expected).Distinct().Count());
        Assert.True(wrong.Count == 0, $"{wrong.Count} of {count} differ:{Environment.NewLine}{string.Join(Environment.NewLine, wrong.Take(15))}");
    }

    [Fact]
    public void TheTwoSentencesAreTheOnesTheApiAnswersWith()
    {
        Assert.Equal("You cannot deactivate your own account or remove your own administrator role.", Messages.CannotLockOutSelf);
        Assert.Equal("At least one active administrator must remain.", Messages.LastAdminRequired);
    }
}
