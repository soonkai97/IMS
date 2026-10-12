using Ims.Api.Tests.Harness;
using Ims.Api.Tests.Support;
using Ims.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ims.Api.Tests.Tests.Schema;

/// <summary>
/// The keys, foreign keys and unique indexes of the migrated database are pinned in <see cref="SchemaPin"/>. A change to any of them (the id
/// type, a key column, a foreign key, a filter) fails here and names the line, so it is made on purpose and the pin is edited in the same pull request.
/// </summary>
[Collection(ApiCollection.Name)]
public class SchemaShapeTests(ApiFixture fixture)
{
    private static string[] Lines(string text) =>
        text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Order(StringComparer.Ordinal).ToArray();

    [Fact]
    public async Task The_migrated_database_has_exactly_the_pinned_tables_keys_foreign_keys_unique_indexes_triggers_and_isolation()
    {
        var actual = await SchemaShape.ReadAsync(fixture.Database.ConnectionString);
        var pinned = Lines(SchemaPin.Expected);

        var added = actual.Except(pinned).ToList();
        var removed = pinned.Except(actual).ToList();

        Assert.True(added.Count == 0 && removed.Count == 0,
            $"The schema no longer matches SchemaPin.\nNew in the database:\n{string.Join('\n', added)}\nPinned but gone:\n{string.Join('\n', removed)}");
    }

    [Fact]
    public async Task The_model_has_no_change_that_a_migration_does_not_carry()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(fixture.Database.ConnectionString).Options;
        await using var db = new AppDbContext(options, new FrozenClock());

        Assert.False(db.Database.HasPendingModelChanges(), "The entity model differs from the last migration snapshot: add a migration.");
    }
}
