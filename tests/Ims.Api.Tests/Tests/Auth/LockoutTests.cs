using System.Net;
using Ims.Api.Tests.Builders;
using Ims.Api.Tests.Harness;
using Ims.Api.Tests.Support;
using Ims.Application.Features.Auth;
using Ims.Domain;

namespace Ims.Api.Tests.Tests.Auth;

[Collection(ApiCollection.Name)]
public class LockoutTests(ApiFixture fixture)
{
    private const string GenericFailure = "Invalid username or password.";
    private readonly AccountFactory _accounts = new(fixture);

    [Fact]
    public async Task Five_wrong_passwords_lock_the_account_and_even_the_correct_password_is_refused()
    {
        var account = await _accounts.CreateAsync(Roles.Clerk);
        var client = fixture.Anonymous();

        for (var attempt = 1; attempt <= 5; attempt++)
        {
            var failure = await client.Auth.LoginAsync(account.Username, "wrong-" + attempt);
            Assert.Equal(GenericFailure, failure.ShouldBeProblem(HttpStatusCode.Unauthorized).Detail);
        }

        var whileLocked = await client.Auth.LoginAsync(account.Username, account.Password);
        var problem = whileLocked.ShouldBeProblem(HttpStatusCode.Unauthorized);
        Assert.Contains("locked", problem.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Four_wrong_passwords_do_not_lock_and_a_good_login_resets_the_counter()
    {
        var account = await _accounts.CreateAsync(Roles.Clerk);
        var client = fixture.Anonymous();

        for (var attempt = 0; attempt < 4; attempt++) (await client.Auth.LoginAsync(account.Username, "wrong")).ShouldBe(HttpStatusCode.Unauthorized);
        (await client.Auth.LoginAsync(account.Username, account.Password)).ShouldBeOk();

        // Four more wrong passwords after a success: still not locked, because the counter started again.
        for (var attempt = 0; attempt < 4; attempt++) (await client.Auth.LoginAsync(account.Username, "wrong")).ShouldBe(HttpStatusCode.Unauthorized);
        (await client.Auth.LoginAsync(account.Username, account.Password)).ShouldBeOk();
    }

    [Fact]
    public async Task Locking_one_account_does_not_affect_another()
    {
        var locked = await _accounts.CreateAsync(Roles.Clerk);
        var other = await _accounts.CreateAsync(Roles.Clerk);
        var client = fixture.Anonymous();

        for (var attempt = 0; attempt < 5; attempt++) await client.Auth.LoginAsync(locked.Username, "wrong");

        (await client.Auth.LoginAsync(other.Username, other.Password)).ShouldBeOk();
    }

    [Fact]
    public async Task The_lock_ends_when_the_lockout_time_has_passed()
    {
        var account = await _accounts.CreateAsync(Roles.Clerk);
        var client = fixture.Anonymous();
        for (var attempt = 0; attempt < 5; attempt++) await client.Auth.LoginAsync(account.Username, "wrong");
        (await client.Auth.LoginAsync(account.Username, account.Password)).ShouldBe(HttpStatusCode.Unauthorized);

        // The test cannot wait 15 minutes, so it moves the lock into the past directly in the database.
        var changed = await fixture.Database.ExecuteAsync(
            "UPDATE [User] SET LockoutUntil = DATEADD(MINUTE, -1, SYSUTCDATETIME()) WHERE Username = @u", ("@u", account.Username));
        Assert.Equal(1, changed);

        (await client.Auth.LoginAsync(account.Username, account.Password)).Data<AuthResult>();
    }

    [Fact]
    public async Task Failed_attempts_lockout_and_success_are_written_to_the_audit_log()
    {
        var account = await _accounts.CreateAsync(Roles.Supervisor);
        var client = fixture.Anonymous();
        for (var attempt = 0; attempt < 5; attempt++) await client.Auth.LoginAsync(account.Username, "wrong");
        await client.Auth.LoginAsync(account.Username, account.Password); // refused: locked

        var admin = await fixture.AsAsync(Roles.Admin);
        var entries = (await admin.Audit.QueryAsync(actor: account.Username)).Data<AuditPage>().Items;

        Assert.Equal(4, entries.Count(e => e.Action == AuditActions.LoginFailed));
        Assert.Single(entries, e => e.Action == AuditActions.LoginLockout);
        Assert.Single(entries, e => e.Action == AuditActions.LoginLocked);
    }
}
