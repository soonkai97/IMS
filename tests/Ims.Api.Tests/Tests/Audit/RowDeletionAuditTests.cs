using System.Text.Json;
using Ims.Api.Tests.Support;
using Ims.Api.Tests.Tests.PublicIds;
using Ims.Domain;
using Ims.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ims.Api.Tests.Tests.Audit;

/// <summary>
/// A soft delete of a row-audited entity leaves a 'deleted' audit row in the same save: the actor, the entity key (public id where the audit names it so), the whole
/// before-image and the time. Both ways a row is soft-deleted are covered: a physical delete that the context turns into REC_DELETED = 1, and the domain's own
/// MarkDeleted. All on a real SQL Server database, through the DbContext (no endpoint deletes these entities yet).
/// </summary>
public class RowDeletionAuditTests(PublicIdDatabase fixture) : IClassFixture<PublicIdDatabase>
{
    private static readonly DateTime CreatedAtUtc = new(2026, 10, 15, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime ChangedAtUtc = new(2026, 10, 16, 8, 30, 0, DateTimeKind.Utc);

    private readonly FixedUser _creator = new(1, Unique.Username("creator"));
    private readonly FixedUser _deleter = new(2, Unique.Username("deleter"));

    public static TheoryData<string> AuditedByPublicId =>
        new() { nameof(Partner), nameof(Category), nameof(Uom), nameof(Warehouse), nameof(Product), nameof(User), nameof(ApprovalRule) };

    private AppDbContext CreatorContext() => fixture.NewContext(user: _creator, clock: new FrozenClock(CreatedAtUtc));

    private AppDbContext DeleterContext() => fixture.NewContext(user: _deleter, clock: new FrozenClock(ChangedAtUtc));

    /// <summary>Saves one new row of the named kind (a product gets its own category and unit) and returns the saved entity.</summary>
    private async Task<PublicEntity> CreateAsync(string kind)
    {
        await using var db = CreatorContext();
        PublicEntity entity;
        switch (kind)
        {
            case nameof(Partner): entity = Partner.Create(PartnerType.Supplier, Unique.Name("Partner")); break;
            case nameof(Category): entity = Category.Create(Unique.Name("Cat")); break;
            case nameof(Uom): entity = Uom.Create(Unique.Code("U")); break;
            case nameof(Warehouse): entity = Warehouse.Create(Unique.Code("W"), Unique.Name("Warehouse")); break;
            case nameof(User):
                var username = Unique.Username();
                entity = User.Create(username, Unique.Email(username), "Deleted soon", Roles.Clerk, "hash"); break;
            case nameof(ApprovalRule): entity = ApprovalRule.Create(DocumentType.Issue, 1000m, null, Roles.Supervisor); break;
            case nameof(Product):
                var category = Category.Create(Unique.Name("Cat"));
                var unit = Uom.Create(Unique.Code("U"));
                db.AddRange(category, unit);
                await db.SaveChangesAsync();
                entity = Product.Create(Unique.Sku(), Unique.Name("Product"), category, unit, 5m); break;
            default: throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
        }
        db.Add(entity);
        await db.SaveChangesAsync();
        return entity;
    }

    private async Task<List<AuditLog>> RowsOfAsync(string entityType, string entityId)
    {
        await using var db = fixture.NewContext();
        return await db.Set<AuditLog>().Where(a => a.EntityType == entityType && a.EntityId == entityId).OrderBy(a => a.Id).ToListAsync();
    }

    private async Task DeleteAsync(PublicEntity saved, Action<AppDbContext, PublicEntity> how)
    {
        await using var db = DeleterContext();
        var entity = (PublicEntity)(await db.FindAsync(saved.GetType(), saved.Id))!;
        how(db, entity);
        await db.SaveChangesAsync();
    }

    private async Task AssertDeletedRowAsync(PublicEntity saved, string kind)
    {
        var rows = await RowsOfAsync(kind, saved.PublicId.ToString());

        Assert.Equal([AuditActions.RowCreated, AuditActions.RowDeleted], rows.Select(r => r.Action));
        var deleted = rows[1];
        Assert.Equal(_deleter.Username, deleted.Actor);
        Assert.Equal(ChangedAtUtc, deleted.At);
        Assert.Null(deleted.After);
        using var before = JsonDocument.Parse(deleted.Before!);
        Assert.Equal(saved.PublicId, before.RootElement.GetProperty(nameof(PublicEntity.PublicId)).GetGuid()); // the whole row as it was, not only what changed
        Assert.False(before.RootElement.TryGetProperty(nameof(AuditableEntity.RecDeleted), out _), "The audit columns stay out of the payload.");
        Assert.Equal(_creator.Username, rows[0].Actor);
        Assert.Equal(CreatedAtUtc, rows[0].At);
        Assert.Equal(1, await fixture.Database.ScalarAsync<int>($"SELECT REC_DELETED FROM [{kind}] WHERE Id = @id", ("@id", saved.Id))); // and the row is soft-deleted
        Assert.Equal(_deleter.UserId, await fixture.Database.ScalarAsync<int>($"SELECT REC_REVISOR FROM [{kind}] WHERE Id = @id", ("@id", saved.Id)));
    }

    [Theory]
    [MemberData(nameof(AuditedByPublicId))]
    public async Task Removing_a_row_writes_a_deleted_audit_row_named_by_its_public_id(string kind)
    {
        var saved = await CreateAsync(kind);

        await DeleteAsync(saved, (db, entity) => db.Remove(entity));

        await AssertDeletedRowAsync(saved, kind);
    }

    [Theory]
    [MemberData(nameof(AuditedByPublicId))]
    public async Task Marking_a_row_deleted_writes_a_deleted_audit_row_named_by_its_public_id(string kind)
    {
        var saved = await CreateAsync(kind);

        await DeleteAsync(saved, (_, entity) => entity.MarkDeleted());

        await AssertDeletedRowAsync(saved, kind);
    }

    [Fact]
    public async Task A_row_changed_and_deleted_in_one_save_gets_one_deleted_row_with_the_values_from_before()
    {
        var saved = (Partner)await CreateAsync(nameof(Partner));

        await DeleteAsync(saved, (db, entity) =>
        {
            ((Partner)entity).Update(PartnerType.Customer, "Renamed on the way out", null, false);
            db.Remove(entity);
        });

        var rows = await RowsOfAsync(nameof(Partner), saved.PublicId.ToString());
        Assert.Equal([AuditActions.RowCreated, AuditActions.RowDeleted], rows.Select(r => r.Action)); // no 'updated' row beside it
        using var before = JsonDocument.Parse(rows[1].Before!);
        Assert.Equal(saved.Name, before.RootElement.GetProperty(nameof(Partner.Name)).GetString());
    }

    [Fact]
    public async Task A_change_without_a_delete_is_still_an_updated_row_and_nothing_else()
    {
        var saved = (Partner)await CreateAsync(nameof(Partner));

        await DeleteAsync(saved, (_, entity) => ((Partner)entity).Update(saved.Type, "Plain rename", null, true));

        var rows = await RowsOfAsync(nameof(Partner), saved.PublicId.ToString());
        Assert.Equal([AuditActions.RowCreated, AuditActions.RowUpdated], rows.Select(r => r.Action));
    }

    [Fact]
    public async Task Barcodes_and_unit_conversions_taken_off_a_product_are_audited_as_deleted_by_their_integer_key()
    {
        await using var db = CreatorContext();
        var category = Category.Create(Unique.Name("Cat"));
        var baseUnit = Uom.Create(Unique.Code("B"));
        var box = Uom.Create(Unique.Code("X"));
        db.AddRange(category, baseUnit, box);
        await db.SaveChangesAsync();
        var product = Product.Create(Unique.Sku(), Unique.Name("Product"), category, baseUnit, 0m);
        var barcode = Unique.Barcode();
        product.SetBarcodes([barcode]);
        product.SetConversions([new ConversionDefinition(box.Id, baseUnit.Id, 12m)]);
        db.Add(product);
        await db.SaveChangesAsync();
        var barcodeId = product.Barcodes.Single().Id.ToString();
        var conversionId = product.Conversions.Single().Id.ToString();

        await using (var edit = DeleterContext())
        {
            var loaded = await edit.Set<Product>().Include(p => p.Barcodes).Include(p => p.Conversions).SingleAsync(p => p.Id == product.Id);
            loaded.SetBarcodes([]);
            loaded.SetConversions([]);
            await edit.SaveChangesAsync();
        }

        var barcodeRow = Assert.Single(await RowsOfAsync(nameof(ProductBarcode), barcodeId), r => r.Action == AuditActions.RowDeleted);
        Assert.Equal(_deleter.Username, barcodeRow.Actor);
        Assert.Contains(barcode, barcodeRow.Before);
        var conversionRow = Assert.Single(await RowsOfAsync(nameof(UomConversion), conversionId), r => r.Action == AuditActions.RowDeleted);
        Assert.Equal(_deleter.Username, conversionRow.Actor);
        Assert.Null(conversionRow.After);
    }
}
