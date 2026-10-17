using Ims.Api.Tests.Harness;
using Ims.Api.Tests.Support;
using Ims.Domain;
using Ims.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Ims.Api.Tests.Tests.Migrations;

/// <summary>
/// Migration <c>DropLegacyRoleColumns</c> (RP-4b) run against a database that already holds users and approval rules of every role: no user changes role or scope and no rule changes
/// tier, the two role strings go, the Administrator-only rules that now widen are named once in the audit log, a role text that disagrees with the role row stops it without a trace,
/// and it goes down (the strings come back from the rows) and up again, as the runner and as the scripts a DBA generates. Each test has its own throw-away database.
/// </summary>
public class LegacyRoleColumnsMigrationTests
{
    private const string Before = "20261012031930_AddPublicIds";
    private const string RolesTables = "20261016100511_AddRolesAndPermissions";
    private const string Latest = "20261017125040_DropLegacyRoleColumns";
    private const string Widened = "approval-rules.widened";

    /// <summary>(name, role, active, deleted): one user of each role, an inactive one and a soft-deleted one.</summary>
    private static readonly (string Name, string Role, bool Active, bool Deleted)[] Users =
    [
        ("admin", Roles.Admin, true, false), ("owner", Roles.Owner, true, false), ("supervisor", Roles.Supervisor, true, false),
        ("staff", Roles.WarehouseStaff, true, false), ("clerk", Roles.Clerk, true, false),
        ("away", Roles.Admin, false, false), ("gone", Roles.Supervisor, true, true)
    ];

    /// <summary>(role, active, deleted): one rule per role, an inactive Admin rule and a soft-deleted one.</summary>
    private static readonly (string Role, bool Active, bool Deleted)[] Rules =
    [
        (Roles.Admin, true, false), (Roles.Owner, true, false), (Roles.Supervisor, true, false), (Roles.WarehouseStaff, true, false), (Roles.Clerk, true, false),
        (Roles.Admin, false, false), (Roles.Admin, true, true)
    ];

    private static async Task MigrateAsync(TestDatabase database, string target)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(database.ConnectionString).Options;
        await using var db = new AppDbContext(options, new FrozenClock());
        await db.GetService<IMigrator>().MigrateAsync(target);
    }

    /// <summary>The script a DBA generates and runs by hand (<c>dotnet ef migrations script</c>), one batch per GO: it must do what the migration runner does.</summary>
    private static async Task RunScriptAsync(TestDatabase database, string from, string to)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(database.ConnectionString).Options;
        await using var db = new AppDbContext(options, new FrozenClock());
        foreach (var batch in System.Text.RegularExpressions.Regex.Split(db.GetService<IMigrator>().GenerateScript(from, to), @"^\s*GO\s*$", System.Text.RegularExpressions.RegexOptions.Multiline))
            if (!string.IsNullOrWhiteSpace(batch)) await database.ExecuteAsync(batch);
    }

    /// <summary>A database as the previous release left it (migrated up to the roles tables, with its users and rules), handed to the test and dropped afterwards.</summary>
    private static async Task WithPreviousReleaseAsync(Func<TestDatabase, Task> test)
    {
        var database = TestDatabase.CreateNew();
        try
        {
            await MigrateAsync(database, Before);
            foreach (var (name, role, active, deleted) in Users)
                await database.ExecuteAsync(
                    "INSERT [User] (Username, Email, DisplayName, PasswordHash, Role, IsActive, FailedLoginCount, REC_DELETED) VALUES (@n, @e, @n, N'x', @r, @a, 0, @d)",
                    ("@n", name), ("@e", $"{name}@example.test"), ("@r", role), ("@a", active), ("@d", deleted));
            foreach (var (role, active, deleted) in Rules)
                await database.ExecuteAsync(
                    "INSERT ApprovalRule (DocType, AmountThreshold, QtyThreshold, RoleRequired, IsActive, REC_DELETED) VALUES (1, 100, NULL, @r, @a, @d)",
                    ("@r", role), ("@a", active), ("@d", deleted));
            await MigrateAsync(database, RolesTables);
            await test(database);
        }
        finally
        {
            await database.DropAsync();
        }
    }

    private static Task<bool> ExistsAsync(TestDatabase database, string table, string column) =>
        database.ScalarAsync<bool>("SELECT IIF(COL_LENGTH(@t, @c) IS NULL, 0, 1)", ("@t", table), ("@c", column));

    private static Task<int> WidenedRowsAsync(TestDatabase database) =>
        database.ScalarAsync<int>("SELECT COUNT(*) FROM AuditLogs WHERE Action = @a", ("@a", Widened));

    private static Task<string> NewestWidenedAsync(TestDatabase database) =>
        database.ScalarAsync<string>("SELECT TOP (1) [After] FROM AuditLogs WHERE Action = @a ORDER BY Id DESC", ("@a", Widened));

    /// <summary>Who is who and what is what, in the columns that stay: role code through the role row, the scope flag, the active flag, and each rule's tier.</summary>
    private static async Task<(string[] Users, string[] Rules)> SnapshotAsync(TestDatabase database)
    {
        var users = await database.ScalarAsync<string>(
            "SELECT STRING_AGG(CONCAT(u.Username, '|', r.Code, '|', u.AllWarehouses, '|', u.IsActive, '|', u.REC_DELETED), ';') WITHIN GROUP (ORDER BY u.Username) FROM [User] u JOIN Role r ON r.Id = u.RoleId");
        var rules = await database.ScalarAsync<string>(
            "SELECT STRING_AGG(CONCAT(Id, '|', Tier, '|', IsActive, '|', REC_DELETED), ';') WITHIN GROUP (ORDER BY Id) FROM ApprovalRule");
        return (users.Split(';'), rules.Split(';'));
    }

    [Fact]
    public Task Nobody_changes_role_or_scope_and_no_rule_changes_tier_and_the_two_strings_are_gone() => WithPreviousReleaseAsync(async database =>
    {
        var before = await SnapshotAsync(database);

        await MigrateAsync(database, Latest);

        Assert.False(await ExistsAsync(database, "User", "Role"));
        Assert.False(await ExistsAsync(database, "ApprovalRule", "RoleRequired"));
        var after = await SnapshotAsync(database);
        Assert.Equal(before.Users, after.Users);
        Assert.Equal(before.Rules, after.Rules);
        Assert.Equal(0, await database.ScalarAsync<int>("SELECT COUNT(*) FROM [User] u JOIN Role r ON r.Id = u.RoleId WHERE u.AllWarehouses <> IIF(r.Code IN (N'Admin', N'Owner'), 1, 0)")); // the scope the old rule gave
    });

    [Fact]
    public Task The_inactive_administrator_rule_is_named_and_the_active_one_the_RP2_row_already_named_is_not_repeated() => WithPreviousReleaseAsync(async database =>
    {
        var rows = await WidenedRowsAsync(database);
        var active = await database.ScalarAsync<Guid>("SELECT PublicId FROM ApprovalRule WHERE RoleRequired = N'Admin' AND IsActive = 1 AND REC_DELETED = 0");
        var inactive = await database.ScalarAsync<Guid>("SELECT PublicId FROM ApprovalRule WHERE RoleRequired = N'Admin' AND IsActive = 0 AND REC_DELETED = 0");
        Assert.Contains(active.ToString(), await NewestWidenedAsync(database), StringComparison.OrdinalIgnoreCase); // RP-2 named it

        await MigrateAsync(database, Latest);

        Assert.Equal(rows + 1, await WidenedRowsAsync(database));
        Assert.Equal($$"""{"count":0,"ruleIds":[],"inactiveRuleIds":["{{inactive}}"]}""", await NewestWidenedAsync(database), ignoreCase: true); // the soft-deleted Admin rule widens nothing
    });

    [Fact]
    public Task Administrator_rules_saved_after_RP2_are_named_with_their_ids_in_ascending_order() => WithPreviousReleaseAsync(async database =>
    {
        // Saved by the RP-4a release, which still wrote RoleRequired and Tier together.
        await database.ExecuteAsync("INSERT ApprovalRule (DocType, AmountThreshold, QtyThreshold, RoleRequired, Tier, IsActive) VALUES (1, 900, NULL, N'Admin', 2, 1), (1, 800, NULL, N'Admin', 2, 1)");
        var ids = await database.ScalarAsync<string>("SELECT STRING_AGG(CONCAT('\"', PublicId, '\"'), ',') WITHIN GROUP (ORDER BY Id) FROM ApprovalRule WHERE AmountThreshold IN (900, 800)");

        await MigrateAsync(database, Latest);

        Assert.Contains($$"""{"count":2,"ruleIds":[{{ids}}],""", await NewestWidenedAsync(database), StringComparison.OrdinalIgnoreCase);
    });

    [Fact]
    public Task Down_puts_the_strings_back_from_the_rows_and_up_again_changes_nothing() => WithPreviousReleaseAsync(async database =>
    {
        await MigrateAsync(database, Latest);
        // A user on a role of their own (the previous release knew only five names, so this can only exist once the strings are gone).
        await database.ExecuteAsync("INSERT Role (Code, Name, Description, IsSystem, IsActive) VALUES (N'Auditor', N'Auditor', N'', 0, 1)");
        await database.ExecuteAsync(
            "INSERT [User] (Username, Email, DisplayName, PasswordHash, RoleId, AllWarehouses, IsActive, FailedLoginCount) SELECT N'auditor', N'auditor@example.test', N'auditor', N'x', Id, 0, 1, 0 FROM Role WHERE Code = N'Auditor'");
        var migrated = await SnapshotAsync(database);

        await MigrateAsync(database, RolesTables);

        // The string is the role row's code for a system role and the least privilege for a custom one; the rule's role is the system role that holds its tier.
        Assert.Equal(0, await database.ScalarAsync<int>("SELECT COUNT(*) FROM [User] u JOIN Role r ON r.Id = u.RoleId WHERE r.IsSystem = 1 AND u.Role <> r.Code"));
        Assert.Equal(Roles.WarehouseStaff, await database.ScalarAsync<string>("SELECT Role FROM [User] WHERE Username = N'auditor'"));
        Assert.Equal(0, await database.ScalarAsync<int>("SELECT COUNT(*) FROM ApprovalRule WHERE RoleRequired <> IIF(Tier = 2, N'Owner', N'Supervisor')"));
        Assert.Equal(0, await database.ScalarAsync<int>("SELECT COUNT(*) FROM __EFMigrationsHistory WHERE MigrationId = @m", ("@m", Latest)));

        await MigrateAsync(database, Latest);

        var again = await SnapshotAsync(database);
        Assert.Equal(migrated.Users, again.Users);
        Assert.Equal(migrated.Rules, again.Rules);
    });

    [Fact]
    public Task The_generated_scripts_go_up_down_and_up_again_as_the_runner_does() => WithPreviousReleaseAsync(async database =>
    {
        await RunScriptAsync(database, RolesTables, Latest);
        var up = await SnapshotAsync(database);
        var rows = await WidenedRowsAsync(database);

        await RunScriptAsync(database, Latest, RolesTables); // Down: this failed with "Invalid column name" while the update shared a batch with the column it reads
        Assert.True(await ExistsAsync(database, "User", "Role"));
        Assert.Equal(0, await database.ScalarAsync<int>("SELECT COUNT(*) FROM [User] u JOIN Role r ON r.Id = u.RoleId WHERE u.Role <> r.Code"));
        Assert.Equal(0, await database.ScalarAsync<int>("SELECT COUNT(*) FROM ApprovalRule WHERE RoleRequired <> IIF(Tier = 2, N'Owner', N'Supervisor')"));

        await RunScriptAsync(database, RolesTables, Latest);
        var again = await SnapshotAsync(database);
        Assert.Equal(up.Users, again.Users);
        Assert.Equal(up.Rules, again.Rules);
        Assert.Equal(rows, await WidenedRowsAsync(database)); // nothing is named twice
    });

    /// <summary>The text of the role must be exactly the code of the role row: another name, other case, a trailing space or tab (the collation would call some of these equal).</summary>
    public static TheoryData<string> Disagreeing => new() { Roles.Clerk, "admin", "Admin ", "Admin\t", "Janitor" };

    [Theory]
    [MemberData(nameof(Disagreeing))]
    public Task A_user_whose_role_text_differs_from_the_role_row_stops_the_migration_and_leaves_no_trace(string text) => WithPreviousReleaseAsync(async database =>
    {
        await database.ExecuteAsync("UPDATE [User] SET Role = @r WHERE Username = N'admin'", ("@r", text)); // its role row is Admin
        var id = await database.ScalarAsync<int>("SELECT Id FROM [User] WHERE Username = N'admin'");
        var rows = await database.ScalarAsync<int>("SELECT COUNT(*) FROM AuditLogs");

        var failure = await Assert.ThrowsAnyAsync<Exception>(() => MigrateAsync(database, Latest));

        Assert.Contains($"User {id} ", failure.ToString());
        Assert.Contains($"role text '{text}'", failure.ToString());
        Assert.Contains("role row is 'Admin'", failure.ToString());
        Assert.True(await ExistsAsync(database, "User", "Role"));
        Assert.True(await ExistsAsync(database, "ApprovalRule", "RoleRequired"));
        Assert.Equal(0, await database.ScalarAsync<int>("SELECT COUNT(*) FROM __EFMigrationsHistory WHERE MigrationId = @m", ("@m", Latest)));
        Assert.Equal(rows, await database.ScalarAsync<int>("SELECT COUNT(*) FROM AuditLogs"));

        await database.ExecuteAsync("UPDATE [User] SET Role = N'Admin' WHERE Username = N'admin'"); // fix the data, run again
        await MigrateAsync(database, Latest);
        Assert.False(await ExistsAsync(database, "User", "Role"));
    });
}
