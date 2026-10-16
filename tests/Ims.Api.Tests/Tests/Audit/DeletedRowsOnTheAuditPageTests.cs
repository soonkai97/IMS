using System.Net;
using System.Text.Json;
using Ims.Api.Tests.Builders;
using Ims.Api.Tests.Harness;
using Ims.Api.Tests.Support;
using Ims.Application.Features.Partners;
using Ims.Domain;
using Ims.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Ims.Api.Tests.Tests.Audit;

/// <summary>
/// A row that is soft-deleted shows up on the audit page like any other change: found by the entity filter and by the public id the client has, readable by Owner
/// and Admin only. (No endpoint deletes these entities yet, so the delete is done through the host's own DbContext; the row-level cases are in <see cref="RowDeletionAuditTests"/>.)
/// </summary>
[Collection(ApiCollection.Name)]
public class DeletedRowsOnTheAuditPageTests(ApiFixture fixture)
{
    private async Task SoftDeleteAsync<T>(Guid publicId) where T : PublicEntity
    {
        using var scope = fixture.Host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Remove(await db.Set<T>().SingleAsync(e => e.PublicId == publicId));
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task A_deleted_partner_is_found_by_its_public_id_with_the_row_as_it_was()
    {
        var admin = await fixture.AsAsync(Roles.Admin);
        var partner = (await admin.Partners.CreateAsync(new PartnerSaveRequest(PartnerType.Supplier, Unique.Name("Partner"), null, true))).Data<PartnerDto>();

        await SoftDeleteAsync<Partner>(partner.Id);

        var trail = (await admin.Audit.QueryAsync(entityType: nameof(Partner), entityId: partner.Id.ToString())).Data<AuditPage>().Items;
        Assert.Equal([AuditActions.RowCreated, AuditActions.RowDeleted], trail.Select(e => e.Action).Order());
        var deleted = trail.Single(e => e.Action == AuditActions.RowDeleted);
        Assert.Equal(partner.Id.ToString(), deleted.EntityId);
        Assert.Equal(AuditEntities.SystemActor, deleted.Actor); // no signed-in caller in a bare scope
        Assert.True(Math.Abs((DateTime.UtcNow - deleted.At).TotalMinutes) < 10);
        Assert.Null(deleted.After);
        using var before = JsonDocument.Parse(deleted.Before!);
        Assert.Equal(partner.Name, before.RootElement.GetProperty(nameof(Partner.Name)).GetString());
        Assert.Equal(partner.Id, before.RootElement.GetProperty(nameof(PublicEntity.PublicId)).GetGuid());
    }

    [Fact]
    public async Task A_deleted_product_is_on_the_audit_page_by_the_action_filter_too()
    {
        var admin = await fixture.AsAsync(Roles.Admin);
        var product = await new ProductBuilder(fixture.Reference).CreateAsync(admin);

        await SoftDeleteAsync<Product>(product.Id);

        var byAction = (await admin.Audit.QueryAsync(entityType: nameof(Product), action: AuditActions.RowDeleted, entityId: product.Id.ToString())).Data<AuditPage>().Items;
        var deleted = Assert.Single(byAction);
        Assert.Contains(product.Sku, deleted.Before);
    }

    [Theory]
    [InlineData(Roles.Supervisor)]
    [InlineData(Roles.WarehouseStaff)]
    [InlineData(Roles.Clerk)]
    public async Task Only_owner_and_admin_may_read_the_audit_page_deletions_included(string role)
    {
        var who = await fixture.AsAsync(role);

        (await who.Audit.QueryAsync(action: AuditActions.RowDeleted)).ShouldBe(HttpStatusCode.Forbidden);
    }
}
