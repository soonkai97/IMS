using Ims.Api.Tests.Support;
using Ims.Domain;
using Ims.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Ims.Api.Tests.Tests.PublicIds;

/// <summary>
/// How the public id is stored: the id the object was made with is the one in the table, the unique index covers soft-deleted rows too, a duplicate is a
/// conflict that says to try again, and the audit trail names a row by its public id once its API has flipped (units; the others still use the integer key). All on a real SQL Server database.
/// </summary>
public class PublicIdStorageTests(PublicIdDatabase fixture) : IClassFixture<PublicIdDatabase>
{
    private static readonly Type[] PublicEntities =
        [typeof(User), typeof(Warehouse), typeof(Partner), typeof(Category), typeof(Uom), typeof(Product), typeof(ApprovalRule), typeof(StockDocument), typeof(Attachment), typeof(Role), typeof(PurchaseOrder), typeof(SalesOrder)];

    private async Task<Uom> AddUomAsync()
    {
        await using var db = fixture.NewContext();
        var uom = Uom.Create(Unique.Code("U"));
        db.Add(uom);
        await db.SaveChangesAsync();
        return uom;
    }

    [Fact]
    public async Task A_saved_entity_keeps_the_id_it_was_made_with()
    {
        var uom = await AddUomAsync();

        Assert.Equal(uom.PublicId, await fixture.Database.ScalarAsync<Guid>("SELECT PublicId FROM Uom WHERE Id = @id", ("@id", uom.Id)));
        await using var db = fixture.NewContext();
        Assert.Equal(uom.PublicId, (await db.Set<Uom>().SingleAsync(u => u.Id == uom.Id)).PublicId);
    }

    [Fact]
    public async Task A_row_inserted_without_an_id_gets_a_random_one_from_the_column_default()
    {
        var code = Unique.Code("R");
        await fixture.Database.ExecuteAsync("INSERT Uom (Code, Name) VALUES (@c, @c)", ("@c", code));

        var id = await fixture.Database.ScalarAsync<Guid>("SELECT PublicId FROM Uom WHERE Code = @c", ("@c", code));

        Assert.NotEqual(Guid.Empty, id);
        await using var db = fixture.NewContext();
        Assert.Equal(id, (await db.Set<Uom>().SingleAsync(u => u.Code == code.ToUpperInvariant())).PublicId); // and the application reads it back
    }

    [Fact]
    public async Task The_model_gives_each_of_the_eleven_entities_an_unfiltered_unique_index_and_nothing_else_has_one()
    {
        await using var db = fixture.NewContext();
        var model = db.GetService<IDesignTimeModel>().Model; // the full model: the runtime one drops relational annotations such as the fill factor
        var withIndex = model.GetEntityTypes()
            .Where(t => t.GetIndexes().Any(i => i.Properties.Any(p => p.Name == nameof(PublicEntity.PublicId))))
            .Select(t => t.ClrType)
            .ToList();
        Assert.Equal(PublicEntities.Select(t => t.Name).Order(), withIndex.Select(t => t.Name).Order());

        foreach (var type in PublicEntities)
        {
            var entity = model.FindEntityType(type)!;
            var index = Assert.Single(entity.GetIndexes(), i => i.Properties.Any(p => p.Name == nameof(PublicEntity.PublicId)));
            Assert.True(index.IsUnique);
            Assert.Equal($"UX_{entity.GetTableName()}_PublicId", index.GetDatabaseName());
            Assert.Null(index.GetFilter()); // a soft-deleted row keeps its id: the other unique indexes are filtered, this one is not
            Assert.Equal(PublicIdColumn.FillFactor, index.GetFillFactor());
            Assert.Equal(1, entity.GetIndexes().Count(i => i.Properties.Count == 1 && i.Properties[0].Name == nameof(PublicEntity.PublicId)));
        }

        var uomCode = model.FindEntityType(typeof(Uom))!.GetIndexes().Single(i => i.Properties.Any(p => p.Name == nameof(Uom.Code)));
        Assert.NotNull(uomCode.GetFilter()); // the rule for the others still holds
    }

    [Fact]
    public async Task An_id_is_never_reused_even_after_its_row_is_deleted_and_the_conflict_says_to_try_again()
    {
        var first = await AddUomAsync();
        await using (var db = fixture.NewContext())
        {
            db.Remove(await db.Set<Uom>().SingleAsync(u => u.Id == first.Id)); // a soft delete: REC_DELETED = 1
            await db.SaveChangesAsync();
        }
        Assert.Equal(1, await fixture.Database.ScalarAsync<int>("SELECT REC_DELETED FROM Uom WHERE Id = @id", ("@id", first.Id)));

        await using var other = fixture.NewContext();
        var second = Uom.Create(Unique.Code("U"));
        other.Add(second);
        other.Entry(second).Property(nameof(PublicEntity.PublicId)).CurrentValue = first.PublicId; // forced: a random GUID meeting a used one cannot be provoked otherwise
        var failure = await Assert.ThrowsAsync<DbUpdateException>(() => other.SaveChangesAsync());

        var problem = new SqlExceptionMapper().Map(failure);

        Assert.NotNull(problem);
        Assert.Equal(409, problem.Status);
        Assert.Equal(Messages.ConflictTitle, problem.Title);
        Assert.Equal(Messages.PublicIdInUse, problem.Detail);
        Assert.DoesNotContain(first.PublicId.ToString(), problem.Detail, StringComparison.OrdinalIgnoreCase); // the server's text names the value: ours does not
    }

    [Fact]
    public async Task Any_other_duplicate_is_still_the_general_conflict()
    {
        var first = await AddUomAsync();
        await using var db = fixture.NewContext();
        db.Add(Uom.Create(first.Code));
        var failure = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());

        var problem = new SqlExceptionMapper().Map(failure);

        Assert.NotNull(problem);
        Assert.Equal(409, problem.Status);
        Assert.Equal(Messages.ConcurrencyConflict, problem.Detail);
    }

    [Fact]
    public async Task A_duplicate_value_that_looks_like_a_public_id_index_is_still_the_general_conflict()
    {
        // The server's text quotes the duplicate value too: "... with unique index 'IX_Category_Name'. The duplicate key value is ('UX_Category_PublicId')".
        var lookalike = $"'{PublicIdColumn.IndexNameOf(nameof(Category))}'";
        await using (var seed = fixture.NewContext())
        {
            seed.Add(Category.Create(lookalike));
            await seed.SaveChangesAsync();
        }
        await using var db = fixture.NewContext();
        db.Add(Category.Create(lookalike));
        var failure = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Contains(lookalike, failure.InnerException!.Message); // the premise: the value is in the server's text

        var problem = new SqlExceptionMapper().Map(failure);

        Assert.NotNull(problem);
        Assert.Equal(Messages.ConcurrencyConflict, problem.Detail);
        Assert.True(PublicIdColumn.IsViolation("Cannot insert duplicate key row in object 'dbo.Product' with unique index 'UX_Product_PublicId'. The duplicate key value is (x)."));
        Assert.False(PublicIdColumn.IsViolation("Cannot insert duplicate key row in object 'dbo.Category' with unique index 'IX_Category_Name'. The duplicate key value is ('UX_Category_PublicId')."));
    }

    [Fact]
    public async Task The_audit_row_of_a_created_unit_names_it_by_public_id_which_the_payload_also_carries()
    {
        var uom = await AddUomAsync();

        await using var db = fixture.NewContext();
        var audit = await db.Set<AuditLog>().SingleAsync(a => a.EntityType == nameof(Uom) && a.EntityId == uom.PublicId.ToString() && a.Action == AuditActions.RowCreated);

        Assert.Contains(uom.PublicId.ToString(), audit.After);
    }
}
