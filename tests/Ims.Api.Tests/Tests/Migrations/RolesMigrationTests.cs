using Ims.Api.Tests.Harness;
using Ims.Api.Tests.Support;
using Ims.Domain;
using Ims.Domain.Tests.Identity.Oracle;
using Ims.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Ims.Api.Tests.Tests.Migrations;

/// <summary>
/// Migration <c>AddRolesAndPermissions</c> (RP-2) run against a database that already holds users and approval rules of every role: every user and every rule comes out
/// with the rights it had before (judged by the very files the old code was frozen in, the roles oracle), the migration goes down and up again, a role string that is not
/// one of the five stops it without leaving a trace, and a fresh database holds the same roles as an upgraded one. Each test has its own throw-away database.
/// </summary>
public class RolesMigrationTests
{
    private const string Before = "20261012031930_AddPublicIds";
    private const string Latest = "20261016100511_AddRolesAndPermissions";
    private const string NotARole = "Janitor";
    private const string Widened = "approval-rules.widened";

    /// <summary>The permissions the catalog gained after this migration, each with a data migration of its own: a database at this migration does not hold them yet.</summary>
    private static readonly string[] AddedLater = [Permissions.ModulesManage];
    private const int Standard = (int)ApproverTier.Standard;
    private const int Large = (int)ApproverTier.Large;

    /// <summary>The users written before the migration: one of each role, an inactive one and a soft-deleted one. (name, role, active, deleted)</summary>
    private static readonly (string Name, string Role, bool Active, bool Deleted)[] OldUsers =
    [
        ("admin", Roles.Admin, true, false), ("owner", Roles.Owner, true, false), ("supervisor", Roles.Supervisor, true, false),
        ("staff", Roles.WarehouseStaff, true, false), ("clerk", Roles.Clerk, true, false),
        ("away", Roles.Admin, false, false), ("gone", Roles.Supervisor, true, true)
    ];

    /// <summary>The rules written before the migration: one per role string, an inactive Admin rule and a soft-deleted one. (role, active, deleted)</summary>
    private static readonly (string Role, bool Active, bool Deleted)[] OldRules =
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

    private static async Task SeedOldDataAsync(TestDatabase database)
    {
        foreach (var (name, role, active, deleted) in OldUsers)
            await database.ExecuteAsync(
                "INSERT [User] (Username, Email, DisplayName, PasswordHash, Role, IsActive, FailedLoginCount, REC_DELETED) VALUES (@n, @e, @n, N'x', @r, @a, 0, @d)",
                ("@n", name), ("@e", $"{name}@example.test"), ("@r", role), ("@a", active), ("@d", deleted));
        foreach (var (role, active, deleted) in OldRules)
            await database.ExecuteAsync(
                "INSERT ApprovalRule (DocType, AmountThreshold, QtyThreshold, RoleRequired, IsActive, REC_DELETED) VALUES (1, 100, NULL, @r, @a, @d)",
                ("@r", role), ("@a", active), ("@d", deleted));
    }

    // ----- What the migrated database must say -----
    private static async Task<IReadOnlySet<string>> PermissionsOfAsync(TestDatabase database, string roleCode)
    {
        var joined = await database.ScalarAsync<string>(
            "SELECT STRING_AGG(p.PermissionCode, ';') FROM RolePermission p JOIN Role r ON r.Id = p.RoleId WHERE r.Code = @c", ("@c", roleCode));
        return (joined ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>The five system roles exist once each, are system and active, hold exactly what <see cref="SystemRoleDefaults"/> says, and nothing else is in the tables.</summary>
    private static async Task AssertSystemRolesAsync(TestDatabase database)
    {
        Assert.Equal(Roles.All.Length, await database.ScalarAsync<int>("SELECT COUNT(*) FROM Role"));
        Assert.Equal(Roles.All.Length, await database.ScalarAsync<int>("SELECT COUNT(DISTINCT PublicId) FROM Role WHERE PublicId <> '00000000-0000-0000-0000-000000000000'"));
        Assert.Equal(Roles.All.Length, await database.ScalarAsync<int>("SELECT COUNT(*) FROM Role WHERE IsSystem = 1 AND IsActive = 1 AND REC_DELETED = 0"));

        var expectedGrants = 0;
        foreach (var code in Roles.All)
        {
            Assert.Equal(1, await database.ScalarAsync<int>("SELECT COUNT(*) FROM Role WHERE Code = @c", ("@c", code)));
            var held = await PermissionsOfAsync(database, code);
            Assert.True(held.SetEquals(SystemRoleDefaults.For(code).Except(AddedLater)), $"{code} holds {string.Join(',', held.Order())}");
            expectedGrants += held.Count;
        }
        Assert.Equal(expectedGrants, await database.ScalarAsync<int>("SELECT COUNT(*) FROM RolePermission WHERE REC_DELETED = 0"));
        Assert.Equal(0, await database.ScalarAsync<int>("SELECT COUNT(*) FROM RolePermission p JOIN Role r ON r.Id = p.RoleId WHERE r.REC_DELETED = 1"));

        // The guard the lean scope asks for in place of a sync: a release that adds a permission to the catalog must ship a migration that gives it to Admin.
        Assert.True((await PermissionsOfAsync(database, Roles.Admin)).SetEquals(PermissionCatalog.Codes.Except(AddedLater)), "Admin must hold every permission of the catalog that existed at this migration.");
    }

    /// <summary>
    /// What a role means in the migrated database is what it meant in the code that the oracle was frozen from: the Domain policies, asked of the permission ROWS the
    /// database holds, answer every question of the oracle (document actions, review tiers, own-only editing) exactly as the committed files say.
    /// </summary>
    private static async Task AssertRolesAnswerAsTheOracleDoesAsync(TestDatabase database)
    {
        var rows = new Dictionary<string, ActorRights>(StringComparer.Ordinal);
        foreach (var code in OracleKeys.RolesWithUnknown)
            rows[code] = new ActorRights(OracleKeys.Me, EffectivePermissions.Compute(await PermissionsOfAsync(database, code)));
        ActorRights RightsOf(string role) => rows[role];

        var documents = OracleTables.DocumentActions.Differences(key => NewBehaviour.DocumentAsFrozen(key, RightsOf));
        Assert.True(documents.Count == 0, $"{documents.Count} document answer(s) differ from the oracle:\n" + string.Join('\n', documents.Take(10)));

        var roles = OracleTables.Roles.Differences(key => NewBehaviour.Role(key, RightsOf));
        Assert.True(roles.Count == 0, $"{roles.Count} role answer(s) differ from the oracle:\n" + string.Join('\n', roles.Take(10)));
    }

    /// <summary>Every user, soft-deleted and inactive ones included, points at the row of the role it had, and sees every warehouse exactly when its role did.</summary>
    private static async Task AssertUsersAsync(TestDatabase database)
    {
        var oracleSeesAll = OracleTables.Roles.Rows().ToDictionary(r => r["role"], r => r["seesAllWarehouses"] == "1");
        Assert.Equal(OldUsers.Length, await database.ScalarAsync<int>("SELECT COUNT(*) FROM [User]"));
        Assert.Equal(0, await database.ScalarAsync<int>("SELECT COUNT(*) FROM [User] u LEFT JOIN Role r ON r.Id = u.RoleId WHERE r.Id IS NULL OR r.Code <> u.Role"));

        foreach (var (name, role, active, deleted) in OldUsers)
        {
            Assert.Equal(role, await database.ScalarAsync<string>("SELECT r.Code FROM [User] u JOIN Role r ON r.Id = u.RoleId WHERE u.Username = @n", ("@n", name)));
            Assert.Equal(role, await database.ScalarAsync<string>("SELECT Role FROM [User] WHERE Username = @n", ("@n", name))); // the string was not touched
            Assert.Equal(oracleSeesAll[role], await database.ScalarAsync<bool>("SELECT AllWarehouses FROM [User] WHERE Username = @n", ("@n", name)));
            Assert.Equal(active, await database.ScalarAsync<bool>("SELECT IsActive FROM [User] WHERE Username = @n", ("@n", name)));
            Assert.Equal(deleted, await database.ScalarAsync<bool>("SELECT REC_DELETED FROM [User] WHERE Username = @n", ("@n", name)));
        }
    }

    /// <summary>
    /// Each rule has the tier the old test gave it. The expected tier is read from the oracle: <c>isLarge</c> of a document of 500 under one rule at 100 that names the
    /// role (rule-sets.csv), which is what "a rule above a supervisor" meant. <c>RoleRequired</c> itself is untouched.
    /// </summary>
    private static async Task AssertRulesAsync(TestDatabase database)
    {
        var oracleLarge = OracleTables.RuleSets.Rows().Where(r => r["amount"] == "500" && !r["rules"].Contains('+') && r["rules"] != "-")
            .ToDictionary(r => r["rules"].Split('@')[0], r => r["isLarge"] == "1");
        Assert.Equal(Roles.All.Length, oracleLarge.Count);
        Assert.Equal(OldRules.Length, await database.ScalarAsync<int>("SELECT COUNT(*) FROM ApprovalRule"));

        var expected = OldRules.Select(r => $"{r.Role}={(oracleLarge[r.Role] ? Large : Standard)}").Order().ToArray();
        var actual = (await database.ScalarAsync<string>("SELECT STRING_AGG(CONCAT(RoleRequired, '=', Tier), ',') FROM ApprovalRule")).Split(',').Order().ToArray();
        Assert.Equal(expected, actual);

        // The documented mapping, in plain words: Owner and Admin are Large, the others Standard.
        Assert.Equal([Roles.Admin, Roles.Owner], oracleLarge.Where(p => p.Value).Select(p => p.Key).Order());
    }

    private static async Task AssertWidenedAuditAsync(TestDatabase database, int rows)
    {
        Assert.Equal(rows, await database.ScalarAsync<int>("SELECT COUNT(*) FROM AuditLogs WHERE Action = @a AND Actor = N'system'", ("@a", Widened)));
        if (rows == 0) return;

        // Only the active, live Admin rule is named: the inactive one and the soft-deleted one do not change who can approve anything.
        var active = await database.ScalarAsync<Guid>("SELECT PublicId FROM ApprovalRule WHERE RoleRequired = N'Admin' AND IsActive = 1 AND REC_DELETED = 0");
        var after = await database.ScalarAsync<string>("SELECT TOP (1) [After] FROM AuditLogs WHERE Action = @a ORDER BY Id DESC", ("@a", Widened));
        Assert.Equal($$"""{"count":1,"ruleIds":["{{active}}"]}""", after, ignoreCase: true);
    }

    private static async Task AssertMigratedAsync(TestDatabase database)
    {
        await AssertSystemRolesAsync(database);
        await AssertRolesAnswerAsTheOracleDoesAsync(database);
        await AssertUsersAsync(database);
        await AssertRulesAsync(database);
    }

    private static Task<bool> ExistsAsync(TestDatabase database, string table, string column = null) =>
        column is null
            ? database.ScalarAsync<bool>("SELECT IIF(OBJECT_ID(@t, N'U') IS NULL, 0, 1)", ("@t", table))
            : database.ScalarAsync<bool>("SELECT IIF(COL_LENGTH(@t, @c) IS NULL, 0, 1)", ("@t", table), ("@c", column));

    private static async Task AssertNothingAddedAsync(TestDatabase database)
    {
        Assert.False(await ExistsAsync(database, "Role"));
        Assert.False(await ExistsAsync(database, "RolePermission"));
        Assert.False(await ExistsAsync(database, "User", "RoleId"));
        Assert.False(await ExistsAsync(database, "User", "AllWarehouses"));
        Assert.False(await ExistsAsync(database, "ApprovalRule", "Tier"));
        Assert.Equal(0, await database.ScalarAsync<int>("SELECT COUNT(*) FROM __EFMigrationsHistory WHERE MigrationId = @m", ("@m", Latest)));
    }

    [Fact]
    public async Task Every_user_and_rule_keeps_its_rights_and_the_migration_goes_down_and_up_again()
    {
        var database = TestDatabase.CreateNew();
        try
        {
            await MigrateAsync(database, Before);
            await SeedOldDataAsync(database);

            await MigrateAsync(database, Latest);
            await AssertMigratedAsync(database);
            await AssertWidenedAuditAsync(database, rows: 1);

            await MigrateAsync(database, Before); // down: the new columns and tables go, the old string columns and every row stay
            await AssertNothingAddedAsync(database);
            Assert.Equal(OldUsers.Length, await database.ScalarAsync<int>("SELECT COUNT(*) FROM [User]"));
            Assert.Equal(OldRules.Length, await database.ScalarAsync<int>("SELECT COUNT(*) FROM ApprovalRule"));
            foreach (var (name, role, _, _) in OldUsers)
                Assert.Equal(role, await database.ScalarAsync<string>("SELECT Role FROM [User] WHERE Username = @n", ("@n", name)));
            Assert.Equal(1, await database.ScalarAsync<int>("SELECT COUNT(*) FROM AuditLogs WHERE Action = @a", ("@a", Widened))); // the audit log is never rewritten

            await MigrateAsync(database, Latest); // and up again: the same answers, and the new run says it widened the rule again
            await AssertMigratedAsync(database);
            await AssertWidenedAuditAsync(database, rows: 2);

            await MigrateAsync(database, Latest); // running it once more is a no-op
            await AssertMigratedAsync(database);
            await AssertWidenedAuditAsync(database, rows: 2);
            Assert.Equal(1, await database.ScalarAsync<int>("SELECT COUNT(*) FROM __EFMigrationsHistory WHERE MigrationId = @m", ("@m", Latest)));
        }
        finally
        {
            await database.DropAsync();
        }
    }

    [Fact]
    public async Task A_migrated_database_and_a_fresh_one_hold_the_same_roles()
    {
        var upgraded = TestDatabase.CreateNew();
        var fresh = TestDatabase.CreateNew();
        try
        {
            await MigrateAsync(upgraded, Before);
            await SeedOldDataAsync(upgraded);
            await MigrateAsync(upgraded, Latest);
            await MigrateAsync(fresh, Latest);

            await AssertSystemRolesAsync(fresh);
            await AssertRolesAnswerAsTheOracleDoesAsync(fresh);
            Assert.Equal(0, await fresh.ScalarAsync<int>("SELECT COUNT(*) FROM [User]"));

            const string dump = """
                SELECT STRING_AGG(CONCAT(r.Code, '|', r.Name, '|', r.Description, '|', r.IsSystem, '|', r.IsActive, '|', g.Codes), CHAR(10)) WITHIN GROUP (ORDER BY r.Code)
                FROM Role r CROSS APPLY (SELECT STRING_AGG(p.PermissionCode, ',') WITHIN GROUP (ORDER BY p.PermissionCode) AS Codes FROM RolePermission p WHERE p.RoleId = r.Id) g
                """;
            Assert.Equal(await upgraded.ScalarAsync<string>(dump), await fresh.ScalarAsync<string>(dump));
        }
        finally
        {
            await upgraded.DropAsync();
            await fresh.DropAsync();
        }
    }

    /// <summary>
    /// Role strings the old code gave no rights at all (it compared them exactly): another name, other case, a trailing space or tab. The database collation would
    /// match the last three to a system role, so they must be refused on purpose.
    /// </summary>
    public static TheoryData<string> NotRoles => new() { NotARole, "admin", "ADMIN", "Admin ", "Admin\t", "owner", "Clerk  ", " Clerk" };

    [Theory]
    [MemberData(nameof(NotRoles))]
    public async Task A_user_whose_role_is_not_one_of_the_five_exactly_stops_the_migration_and_leaves_no_trace(string notARole)
    {
        var database = TestDatabase.CreateNew();
        try
        {
            await MigrateAsync(database, Before);
            await SeedOldDataAsync(database);
            await database.ExecuteAsync(
                "INSERT [User] (Username, Email, DisplayName, PasswordHash, Role, IsActive, FailedLoginCount) VALUES (N'odd', N'odd@example.test', N'odd', N'x', @r, 1, 0)", ("@r", notARole));
            var id = await database.ScalarAsync<int>("SELECT Id FROM [User] WHERE Username = N'odd'");
            Assert.Equal(notARole.Length * 2, await database.ScalarAsync<int>("SELECT DATALENGTH(Role) FROM [User] WHERE Username = N'odd'")); // stored as given

            var failure = await Assert.ThrowsAnyAsync<Exception>(() => MigrateAsync(database, Latest));

            Assert.Contains($"User {id} ", failure.ToString()); // names the user ...
            Assert.Contains($"has the role '{notARole}'", failure.ToString()); // ... and the role it could not map
            await AssertNothingAddedAsync(database); // everything the migration did before it noticed was rolled back, so a new run starts from the same place
            Assert.Equal(notARole.Length * 2, await database.ScalarAsync<int>("SELECT DATALENGTH(Role) FROM [User] WHERE Username = N'odd'"));

            await database.ExecuteAsync("UPDATE [User] SET Role = N'Clerk' WHERE Username = N'odd'"); // fix the data, run again
            await MigrateAsync(database, Latest);
            Assert.Equal(Roles.Clerk, await database.ScalarAsync<string>("SELECT r.Code FROM [User] u JOIN Role r ON r.Id = u.RoleId WHERE u.Username = N'odd'"));
            Assert.False(await database.ScalarAsync<bool>("SELECT AllWarehouses FROM [User] WHERE Username = N'odd'"));
        }
        finally
        {
            await database.DropAsync();
        }
    }

    [Theory]
    [MemberData(nameof(NotRoles))]
    public async Task A_rule_naming_a_role_that_is_not_one_of_the_five_exactly_stops_the_migration_names_the_rule_and_leaves_no_trace(string notARole)
    {
        foreach (var softDeleted in new[] { false, true }) // nothing is guessed, a deleted rule included
        {
            var database = TestDatabase.CreateNew();
            try
            {
                await MigrateAsync(database, Before);
                await SeedOldDataAsync(database);
                await database.ExecuteAsync(
                    "INSERT ApprovalRule (DocType, AmountThreshold, QtyThreshold, RoleRequired, IsActive, REC_DELETED) VALUES (2, NULL, 50, @r, 1, @d)", ("@r", notARole), ("@d", softDeleted));
                var id = await database.ScalarAsync<int>("SELECT MAX(Id) FROM ApprovalRule"); // not by role: the collation would find the Admin rules too

                var failure = await Assert.ThrowsAnyAsync<Exception>(() => MigrateAsync(database, Latest));

                Assert.Contains($"Approval rule {id} ", failure.ToString());
                Assert.Contains($"names the role '{notARole}'", failure.ToString());
                await AssertNothingAddedAsync(database);
                Assert.Equal(0, await database.ScalarAsync<int>("SELECT COUNT(*) FROM AuditLogs WHERE Action = @a", ("@a", Widened)));

                await database.ExecuteAsync("UPDATE ApprovalRule SET RoleRequired = N'Supervisor' WHERE Id = @i", ("@i", id));
                await MigrateAsync(database, Latest);
                Assert.Equal(Standard, await database.ScalarAsync<int>("SELECT Tier FROM ApprovalRule WHERE Id = @i", ("@i", id)));
            }
            finally
            {
                await database.DropAsync();
            }
        }
    }

    [Fact]
    public async Task Many_admin_only_rules_are_all_named_in_the_one_audit_row()
    {
        const int extra = 150; // the list of ids is longer than 8,000 bytes of nvarchar(36) from about 110 rules
        var database = TestDatabase.CreateNew();
        try
        {
            await MigrateAsync(database, Before);
            await SeedOldDataAsync(database);
            await database.ExecuteAsync(
                "INSERT ApprovalRule (DocType, AmountThreshold, QtyThreshold, RoleRequired, IsActive) SELECT TOP (@n) 1, ROW_NUMBER() OVER (ORDER BY (SELECT NULL)), NULL, N'Admin', 1 FROM sys.all_objects",
                ("@n", extra));

            await MigrateAsync(database, Latest);

            var after = await database.ScalarAsync<string>("SELECT [After] FROM AuditLogs WHERE Action = @a", ("@a", Widened));
            using var json = System.Text.Json.JsonDocument.Parse(after);
            Assert.Equal(extra + 1, json.RootElement.GetProperty("count").GetInt32()); // the extra rules and the seeded active Admin rule
            Assert.Equal(extra + 1, json.RootElement.GetProperty("ruleIds").GetArrayLength());
            Assert.Equal(extra + 1, await database.ScalarAsync<int>("SELECT COUNT(*) FROM ApprovalRule WHERE RoleRequired = N'Admin' AND IsActive = 1 AND REC_DELETED = 0 AND Tier = 2"));
        }
        finally
        {
            await database.DropAsync();
        }
    }
}
