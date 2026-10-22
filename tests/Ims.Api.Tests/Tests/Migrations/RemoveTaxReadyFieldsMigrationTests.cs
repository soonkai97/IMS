using System.Text.RegularExpressions;
using Ims.Api.Tests.Harness;
using Ims.Api.Tests.Support;
using Ims.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Ims.Api.Tests.Tests.Migrations;

/// <summary>
/// Migration <c>RemoveTaxReadyFields</c> (part B of the removal of the accounting module): the four columns <c>Product.HsCode</c>, <c>Product.SstCode</c>, <c>Partner.Tin</c> and
/// <c>Partner.SstRegNo</c> are dropped and every other column and row stays. Up discards a value still held in them without a check. Down adds the four back nullable, with the same
/// sizes as <c>AddTaxReadyFields</c>, and EMPTY. A fresh database ends without them, and the generated script does what the runner does. Each test has its own throw-away database.
/// </summary>
public class RemoveTaxReadyFieldsMigrationTests
{
    private const string Before = "20261022122230_RemoveAccountingModule";
    private const string Latest = "20261022141734_RemoveTaxReadyFields";

    private static readonly (string Table, string Column, int Length)[] Columns =
        [("Product", "HsCode", 16), ("Product", "SstCode", 20), ("Partner", "Tin", 20), ("Partner", "SstRegNo", 30)];

    private static async Task MigrateAsync(TestDatabase database, string target)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(database.ConnectionString).Options;
        await using var db = new AppDbContext(options, new FrozenClock());
        await db.GetService<IMigrator>().MigrateAsync(target);
    }

    /// <summary>The script a DBA generates and runs by hand, one batch per GO: it must do what the migration runner does.</summary>
    private static async Task RunScriptAsync(TestDatabase database, string from, string to)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(database.ConnectionString).Options;
        await using var db = new AppDbContext(options, new FrozenClock());
        foreach (var batch in Regex.Split(db.GetService<IMigrator>().GenerateScript(from, to), @"^\s*GO\s*$", RegexOptions.Multiline))
            if (!string.IsNullOrWhiteSpace(batch)) await database.ExecuteAsync(batch);
    }

    private static async Task WithDatabaseAsync(Func<TestDatabase, Task> test)
    {
        var database = TestDatabase.CreateNew();
        try
        {
            await test(database);
        }
        finally
        {
            await database.DropAsync();
        }
    }

    private static Task<int> ColumnCountAsync(TestDatabase database) =>
        database.ScalarAsync<int>("""
            SELECT COUNT(*) FROM sys.columns c JOIN sys.tables t ON t.object_id = c.object_id
            WHERE (t.name = N'Product' AND c.name IN (N'HsCode', N'SstCode')) OR (t.name = N'Partner' AND c.name IN (N'Tin', N'SstRegNo'))
            """);

    /// <summary>Rows in the schema BEFORE the migration, two of them holding tax values, one soft-deleted.</summary>
    private static async Task SeedAsync(TestDatabase database)
    {
        await database.ExecuteAsync("INSERT Uom (Code, Name) VALUES (N'PCS', N'Pieces')");
        await database.ExecuteAsync("INSERT Product (Sku, Name, BaseUomId, SafetyStock, IsActive, HsCode, SstCode) SELECT N'A-1', N'Alpha', Id, 0, 1, N'8471.30', N'ST-01' FROM Uom");
        await database.ExecuteAsync("INSERT Product (Sku, Name, BaseUomId, SafetyStock, IsActive) SELECT N'B-1', N'Beta', Id, 5, 1 FROM Uom");
        await database.ExecuteAsync("INSERT Partner (Type, Name, IsActive, Tin, SstRegNo) VALUES (1, N'Supplier One', 1, N'C2584563200', N'W10-1808-32000123')");
        await database.ExecuteAsync("INSERT Partner (Type, Name, IsActive) VALUES (2, N'Customer One', 1)");
        await database.ExecuteAsync("UPDATE Product SET REC_DELETED = 1 WHERE Sku = N'B-1'");
    }

    [Fact]
    public Task Up_drops_the_four_columns_and_every_row_and_other_column_stays() => WithDatabaseAsync(async database =>
    {
        await MigrateAsync(database, Before);
        await SeedAsync(database);
        Assert.Equal(4, await ColumnCountAsync(database));

        await MigrateAsync(database, Latest);

        Assert.Equal(0, await ColumnCountAsync(database));
        Assert.Equal(2, await database.ScalarAsync<int>("SELECT COUNT(*) FROM Product"));
        Assert.Equal(2, await database.ScalarAsync<int>("SELECT COUNT(*) FROM Partner"));
        Assert.Equal("Alpha", await database.ScalarAsync<string>("SELECT Name FROM Product WHERE Sku = N'A-1'"));
        Assert.Equal(5m, await database.ScalarAsync<decimal>("SELECT SafetyStock FROM Product WHERE Sku = N'B-1'"));
        Assert.Equal(1, await database.ScalarAsync<int>("SELECT COUNT(*) FROM Product WHERE REC_DELETED = 1")); // the soft-deleted row is still there
        Assert.Equal("Supplier One", await database.ScalarAsync<string>("SELECT Name FROM Partner WHERE Type = 1"));
    });

    [Fact]
    public Task Down_adds_the_columns_back_nullable_with_the_old_sizes_and_empty() => WithDatabaseAsync(async database =>
    {
        await MigrateAsync(database, Before);
        await SeedAsync(database);
        await MigrateAsync(database, Latest);

        await MigrateAsync(database, Before); // down

        Assert.Equal(4, await ColumnCountAsync(database));
        foreach (var (table, column, length) in Columns)
        {
            Assert.Equal(length * 2, await database.ScalarAsync<int>("SELECT max_length FROM sys.columns WHERE object_id = OBJECT_ID(@t) AND name = @c", ("@t", table), ("@c", column))); // nvarchar: two bytes a character
            Assert.True(await database.ScalarAsync<bool>("SELECT is_nullable FROM sys.columns WHERE object_id = OBJECT_ID(@t) AND name = @c", ("@t", table), ("@c", column)), $"{table}.{column} must be nullable");
        }
        Assert.Equal(0, await database.ScalarAsync<int>("SELECT COUNT(*) FROM Product WHERE HsCode IS NOT NULL OR SstCode IS NOT NULL")); // the dropped values do not come back
        Assert.Equal(0, await database.ScalarAsync<int>("SELECT COUNT(*) FROM Partner WHERE Tin IS NOT NULL OR SstRegNo IS NOT NULL"));
        Assert.Equal(2, await database.ScalarAsync<int>("SELECT COUNT(*) FROM Product"));
        Assert.Equal(0, await database.ScalarAsync<int>("SELECT COUNT(*) FROM __EFMigrationsHistory WHERE MigrationId = @m", ("@m", Latest)));

        await MigrateAsync(database, Latest); // and up again

        Assert.Equal(0, await ColumnCountAsync(database));
    });

    [Fact]
    public Task After_Down_the_older_AddTaxReadyFields_can_still_be_taken_down() => WithDatabaseAsync(async database =>
    {
        await MigrateAsync(database, Before);
        await MigrateAsync(database, Latest);
        await MigrateAsync(database, Before);

        await MigrateAsync(database, "20261019145353_GrantAccountingExport"); // the Down of AddTaxReadyFields refuses only while a value is held

        Assert.Equal(0, await ColumnCountAsync(database));
    });

    [Fact]
    public Task The_generated_script_does_what_the_runner_does_up_and_down() => WithDatabaseAsync(async database =>
    {
        await MigrateAsync(database, Before);
        await SeedAsync(database);

        await RunScriptAsync(database, Before, Latest);
        Assert.Equal(0, await ColumnCountAsync(database));
        Assert.Equal(2, await database.ScalarAsync<int>("SELECT COUNT(*) FROM Partner"));

        await RunScriptAsync(database, Latest, Before);
        Assert.Equal(4, await ColumnCountAsync(database));
        Assert.Equal(0, await database.ScalarAsync<int>("SELECT COUNT(*) FROM __EFMigrationsHistory WHERE MigrationId = @m", ("@m", Latest)));
    });

    [Fact]
    public Task A_fresh_database_has_none_of_the_four_columns() => WithDatabaseAsync(async database =>
    {
        await MigrateAsync(database, Latest);

        Assert.Equal(0, await ColumnCountAsync(database));
    });
}
