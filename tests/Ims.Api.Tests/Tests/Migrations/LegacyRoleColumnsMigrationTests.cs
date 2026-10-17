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
/// tier, the two role strings go, the Administrator-only rules that now widen are named in one audit row, a role text that disagrees with the role row stops it without a trace, and
/// it goes down (the strings come back from the rows) and up again. Each test has its own throw-away database.
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

    /// <summary>A database as the previous release left it: migrated up to the roles tables, with its users and rules.</summary>
    private static async Task<TestDatabase> PreviousReleaseAsync()
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
            return database;
        }
        catch
        {
            await database.DropAsync();
            throw;
        }
    }

    private static Task<bool> ExistsAsync(TestDatabase database, string table, string column) =>
        database.ScalarAsync<bool>("SELECT IIF(COL_LENGTH(@t, @c) IS NULL, 0, 1)", ("@t", table), ("@c", column));

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
    public async Task Nobody_changes_role_or_scope_and_no_rule_changes_tier_and_the_two_strings_are_gone()
    {
        var database = await PreviousReleaseAsync();
        try
        {
            var before = await SnapshotAsync(database);
            Assert.Equal(Users.Length, before.Users.Length);

            await MigrateAsync(database, Latest);

            Assert.False(await ExistsAsync(database, "User", "Role"));
            Assert.False(await ExistsAsync(database, "ApprovalRule", "RoleRequired"));
            var after = await SnapshotAsync(database);
            Assert.Equal(before.Users, after.Users);
            Assert.Equal(before.Rules, after.Rules);
            // Exactly the role of each user, as the row says, and the scope the old rule gave it.
            foreach (var (name, role, _, _) in Users)
            {
                Assert.Equal(role, await database.ScalarAsync<string>("SELECT r.Code FROM [User] u JOIN Role r ON r.Id = u.RoleId WHERE u.Username = @n", ("@n", name)));
                Assert.Equal(TestScope.SeesAllWarehouses(role), await database.ScalarAsync<bool>("SELECT AllWarehouses FROM [User] WHERE Username = @n", ("@n", name)));
            }
            // The tier of each rule is what the RP-2 migration gave the role it named: Admin and Owner Large, the rest Standard.
            Assert.Equal(2, await database.ScalarAsync<int>("SELECT COUNT(*) FROM ApprovalRule WHERE Tier = 2 AND REC_DELETED = 0 AND IsActive = 1"));
        }
        finally
        {
            await database.DropAsync();
        }
    }

    [Fact]
    public async Task The_inactive_administrator_rule_is_named_and_the_active_one_the_RP2_row_already_named_is_not_repeated()
    {
        var database = await PreviousReleaseAsync();
        try
        {
            var rows = await database.ScalarAsync<int>("SELECT COUNT(*) FROM AuditLogs WHERE Action = @a", ("@a", Widened));
            var active = await database.ScalarAsync<Guid>("SELECT PublicId FROM ApprovalRule WHERE RoleRequired = N'Admin' AND IsActive = 1 AND REC_DELETED = 0");
            var inactive = await database.ScalarAsync<Guid>("SELECT PublicId FROM ApprovalRule WHERE RoleRequired = N'Admin' AND IsActive = 0 AND REC_DELETED = 0");
            Assert.Contains(active.ToString(), await database.ScalarAsync<string>("SELECT [After] FROM AuditLogs WHERE Action = @a", ("@a", Widened)), StringComparison.OrdinalIgnoreCase); // RP-2 named it

            await MigrateAsync(database, Latest);

            Assert.Equal(rows + 1, await database.ScalarAsync<int>("SELECT COUNT(*) FROM AuditLogs WHERE Action = @a AND Actor = N'system'", ("@a", Widened)));
            var after = await database.ScalarAsync<string>("SELECT TOP (1) [After] FROM AuditLogs WHERE Action = @a ORDER BY Id DESC", ("@a", Widened));
            Assert.Equal($$"""{"count":0,"ruleIds":[],"inactiveRuleIds":["{{inactive}}"]}""", after, ignoreCase: true); // the soft-deleted Admin rule widens nothing
        }
        finally
        {
            await database.DropAsync();
        }
    }

    [Fact]
    public async Task Administrator_rules_saved_after_RP2_are_named_with_their_ids_in_ascending_order()
    {
        var database = await PreviousReleaseAsync();
        try
        {
            // Saved by the RP-4a release, which still wrote RoleRequired and Tier together. Inserted newest id first by threshold, to show the order is by id.
            await database.ExecuteAsync("INSERT ApprovalRule (DocType, AmountThreshold, QtyThreshold, RoleRequired, Tier, IsActive) VALUES (1, 900, NULL, N'Admin', 2, 1), (1, 800, NULL, N'Admin', 2, 1), (1, 700, NULL, N'Admin', 2, 0)");
            var ids = new List<string>();
            foreach (var threshold in new[] { 900, 800 })
                ids.Add((await database.ScalarAsync<Guid>("SELECT PublicId FROM ApprovalRule WHERE AmountThreshold = @t", ("@t", threshold))).ToString().ToUpperInvariant());
            var inactive = (await database.ScalarAsync<Guid>("SELECT PublicId FROM ApprovalRule WHERE AmountThreshold = 700")).ToString().ToUpperInvariant();

            await MigrateAsync(database, Latest);

            var after = await database.ScalarAsync<string>("SELECT TOP (1) [After] FROM AuditLogs WHERE Action = @a ORDER BY Id DESC", ("@a", Widened));
            using var json = System.Text.Json.JsonDocument.Parse(after);
            Assert.Equal(2, json.RootElement.GetProperty("count").GetInt32());
            Assert.Equal(ids, json.RootElement.GetProperty("ruleIds").EnumerateArray().Select(e => e.GetString()!.ToUpperInvariant()).ToList()); // 900 was inserted before 800: ascending id
            Assert.Contains(inactive, json.RootElement.GetProperty("inactiveRuleIds").EnumerateArray().Select(e => e.GetString()!.ToUpperInvariant()));
        }
        finally
        {
            await database.DropAsync();
        }
    }

    [Fact]
    public async Task No_administrator_rule_means_no_audit_row()
    {
        var database = await PreviousReleaseAsync();
        try
        {
            await database.ExecuteAsync("UPDATE ApprovalRule SET RoleRequired = N'Owner' WHERE RoleRequired = N'Admin'");
            var rows = await database.ScalarAsync<int>("SELECT COUNT(*) FROM AuditLogs WHERE Action = @a", ("@a", Widened));

            await MigrateAsync(database, Latest);

            Assert.Equal(rows, await database.ScalarAsync<int>("SELECT COUNT(*) FROM AuditLogs WHERE Action = @a", ("@a", Widened)));
        }
        finally
        {
            await database.DropAsync();
        }
    }

    /// <summary>The script a DBA generates and runs by hand (<c>dotnet ef migrations script</c>) is one batch per GO: it must do what the migration runner does.</summary>
    private static async Task RunScriptAsync(TestDatabase database, string from, string to)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(database.ConnectionString).Options;
        await using var db = new AppDbContext(options, new FrozenClock());
        var script = db.GetService<IMigrator>().GenerateScript(from, to);
        foreach (var batch in System.Text.RegularExpressions.Regex.Split(script, @"^\s*GO\s*$", System.Text.RegularExpressions.RegexOptions.Multiline))
            if (!string.IsNullOrWhiteSpace(batch)) await database.ExecuteAsync(batch);
    }

    [Fact]
    public async Task The_generated_scripts_go_up_down_and_up_again_as_the_runner_does()
    {
        var database = await PreviousReleaseAsync();
        try
        {
            await RunScriptAsync(database, RolesTables, Latest);
            var up = await SnapshotAsync(database);
            Assert.False(await ExistsAsync(database, "User", "Role"));
            var rows = await database.ScalarAsync<int>("SELECT COUNT(*) FROM AuditLogs WHERE Action = @a", ("@a", Widened));

            await RunScriptAsync(database, Latest, RolesTables); // Down: this failed with "Invalid column name" while the update shared a batch with the column it reads
            Assert.True(await ExistsAsync(database, "User", "Role"));
            Assert.Equal(0, await database.ScalarAsync<int>("SELECT COUNT(*) FROM [User] u JOIN Role r ON r.Id = u.RoleId WHERE u.Role <> r.Code"));
            Assert.Equal(0, await database.ScalarAsync<int>("SELECT COUNT(*) FROM ApprovalRule WHERE RoleRequired <> IIF(Tier = 2, N'Owner', N'Supervisor')"));
            Assert.Equal(0, await database.ScalarAsync<int>("SELECT COUNT(*) FROM __EFMigrationsHistory WHERE MigrationId = @m", ("@m", Latest)));

            await RunScriptAsync(database, RolesTables, Latest);
            var again = await SnapshotAsync(database);
            Assert.Equal(up.Users, again.Users);
            Assert.Equal(up.Rules, again.Rules);
            Assert.Equal(rows, await database.ScalarAsync<int>("SELECT COUNT(*) FROM AuditLogs WHERE Action = @a", ("@a", Widened))); // nothing is named twice
        }
        finally
        {
            await database.DropAsync();
        }
    }

    public async Task Down_puts_the_strings_back_from_the_rows_and_up_again_changes_nothing()
    {
        var database = await PreviousReleaseAsync();
        try
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
            Assert.False(await ExistsAsync(database, "User", "Role"));
        }
        finally
        {
            await database.DropAsync();
        }
    }

    /// <summary>The text of the role must be exactly the code of the role row: another name, other case, a trailing space (the collation would call these equal).</summary>
    public static TheoryData<string> Disagreeing => new() { Roles.Clerk, "admin", "ADMIN", "Admin ", "Admin\t", "Janitor" };

    [Theory]
    [MemberData(nameof(Disagreeing))]
    public async Task A_user_whose_role_text_differs_from_the_role_row_stops_the_migration_and_leaves_no_trace(string text)
    {
        var database = await PreviousReleaseAsync();
        try
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
        }
        finally
        {
            await database.DropAsync();
        }
    }

    [Fact]
    public async Task A_fresh_database_has_neither_string()
    {
        var database = TestDatabase.CreateNew();
        try
        {
            await MigrateAsync(database, Latest);

            Assert.False(await ExistsAsync(database, "User", "Role"));
            Assert.False(await ExistsAsync(database, "ApprovalRule", "RoleRequired"));
            Assert.True(await ExistsAsync(database, "User", "RoleId"));
            Assert.True(await ExistsAsync(database, "ApprovalRule", "Tier"));
            Assert.Equal(0, await database.ScalarAsync<int>("SELECT COUNT(*) FROM AuditLogs WHERE Action = @a", ("@a", Widened)));
        }
        finally
        {
            await database.DropAsync();
        }
    }
}
