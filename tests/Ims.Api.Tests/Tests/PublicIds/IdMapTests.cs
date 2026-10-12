using Ims.Api.Tests.Support;
using Ims.Application;
using Ims.Domain;
using Ims.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ims.Api.Tests.Tests.PublicIds;

/// <summary>The id map and the two look-up specifications, on a real database: batched, scoped, and clear about rows that were soft-deleted.</summary>
public class IdMapTests(PublicIdDatabase fixture) : IClassFixture<PublicIdDatabase>
{
    private async Task<(Warehouse Live, Warehouse Deleted)> AddWarehousesAsync()
    {
        await using var db = fixture.NewContext();
        var live = Warehouse.Create(Unique.Code("L"), "Live");
        var deleted = Warehouse.Create(Unique.Code("D"), "Deleted");
        db.AddRange(live, deleted);
        await db.SaveChangesAsync();
        db.Remove(deleted); // soft delete
        await db.SaveChangesAsync();
        return (live, deleted);
    }

    private static IdMap<Warehouse> MapOver(AppDbContext db) => new(new Repository<Warehouse>(db));

    [Fact]
    public async Task Maps_in_both_directions_and_leaves_out_what_does_not_exist()
    {
        var (live, _) = await AddWarehousesAsync();
        await using var db = fixture.NewContext();
        var map = MapOver(db);

        var toInternal = await map.ToInternalAsync([live.PublicId, Guid.NewGuid(), Guid.Empty]);
        var toPublic = await map.ToPublicAsync([live.Id, int.MaxValue]);

        Assert.Equal(live.Id, Assert.Single(toInternal).Value);
        Assert.Equal(live.PublicId, Assert.Single(toPublic).Value);
    }

    [Fact]
    public async Task A_soft_deleted_row_is_not_found_by_a_client_id_but_is_still_named_in_a_response()
    {
        var (_, deleted) = await AddWarehousesAsync();
        await using var db = fixture.NewContext();
        var map = MapOver(db);

        Assert.Empty(await map.ToInternalAsync([deleted.PublicId]));
        Assert.Equal(deleted.PublicId, (await map.ToPublicAsync([deleted.Id]))[deleted.Id]);
        Assert.Empty(await map.ToInternalAsync([deleted.PublicId])); // what the other direction found does not widen this one
    }

    [Fact]
    public async Task A_thousand_ids_are_one_query_and_asking_again_in_the_same_scope_is_none()
    {
        var (live, _) = await AddWarehousesAsync();
        var counter = new PublicIdDatabase.CommandCounter();
        await using var db = fixture.NewContext(counter);
        var map = MapOver(db);
        var ids = Enumerable.Range(0, 3_000).Select(_ => Guid.NewGuid()).Append(live.PublicId).ToList(); // more than SQL Server's 2,100 parameters

        var first = await map.ToInternalAsync(ids);
        var queriesAfterFirst = counter.Queries;
        var second = await map.ToInternalAsync(ids.Take(5).Append(live.PublicId));

        Assert.Equal(1, queriesAfterFirst);
        Assert.Equal(live.Id, Assert.Single(first).Value);
        Assert.Equal(live.Id, Assert.Single(second).Value);
        Assert.Equal(queriesAfterFirst + 1, counter.Queries); // the five unknown ids are asked again (misses are not remembered), the known one is not

        var third = await map.ToInternalAsync([live.PublicId]);
        Assert.Equal(live.Id, Assert.Single(third).Value);
        Assert.Equal(queriesAfterFirst + 1, counter.Queries);
    }

    [Fact]
    public async Task By_public_id_finds_one_row_and_a_deleted_row_is_hidden()
    {
        var (live, deleted) = await AddWarehousesAsync();
        await using var db = fixture.NewContext();
        var repository = new Repository<Warehouse>(db);

        var found = await repository.FirstOrDefaultAsync(new ByPublicIdSpec<Warehouse>(live.PublicId));
        Assert.Equal(live.Id, found?.Id);
        Assert.Equal(EntityState.Unchanged, db.Entry(found!).State); // tracked by default

        Assert.Null(await repository.FirstOrDefaultAsync(new ByPublicIdSpec<Warehouse>(deleted.PublicId)));
        Assert.Null(await repository.FirstOrDefaultAsync(new ByPublicIdSpec<Warehouse>(Guid.NewGuid())));
    }

    [Fact]
    public async Task Public_ids_finds_several_rows_in_one_query_without_tracking_when_asked()
    {
        var (live, deleted) = await AddWarehousesAsync();
        var counter = new PublicIdDatabase.CommandCounter();
        await using var db = fixture.NewContext(counter);
        var repository = new Repository<Warehouse>(db);

        var rows = await repository.ListAsync(new PublicIdsSpec<Warehouse>([live.PublicId, deleted.PublicId, Guid.NewGuid()], tracked: false));

        Assert.Equal(live.Id, Assert.Single(rows).Id);
        Assert.Equal(1, counter.Queries);
        Assert.Empty(db.ChangeTracker.Entries());
    }
}
