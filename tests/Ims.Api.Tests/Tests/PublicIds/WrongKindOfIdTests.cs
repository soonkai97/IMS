using System.Net;
using Ims.Api.Tests.Builders;
using Ims.Api.Tests.Harness;
using Ims.Api.Tests.Support;
using Ims.Application;
using Ims.Application.Features.Catalog;
using Ims.Application.Features.Documents;
using Ims.Application.Features.Partners;
using Ims.Application.Features.Settings;
using Ims.Application.Features.Stock;
using Ims.Application.Features.Users;
using Ims.Application.Features.Warehouses;
using Ims.Domain;

namespace Ims.Api.Tests.Tests.PublicIds;

/// <summary>
/// GUID plan, ADR section 6.1 and 6.2: a public id is only found in the table it belongs to. The id of another kind of entity, a made-up id, the
/// empty id and a cursor changed by one character are all answered like "nothing here" (404 or 400 with one body), so an id or a cursor can neither
/// be used to reach another entity nor tell its kind. The document, product and attachment routes have their own tests next to their flips.
/// </summary>
[Collection(ApiCollection.Name)]
public class WrongKindOfIdTests(ApiFixture fixture)
{
    [Fact]
    public async Task The_update_route_of_every_master_data_entity_answers_the_id_of_another_kind_like_an_unknown_id_with_one_404()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        var account = await new AccountFactory(fixture).CreateAsync(Roles.Clerk);
        var warehouse = fixture.Reference.DefaultWarehouseId;
        var routes = new Dictionary<string, object>
        {
            ["warehouses"] = new WarehouseSaveRequest(Unique.Code("W"), Unique.Name("Warehouse"), null, null, true),
            ["partners"] = new PartnerSaveRequest(PartnerType.Supplier, Unique.Name("Partner"), null, true),
            ["categories"] = new CategorySaveRequest(Unique.Name("Cat")),
            ["units"] = new UomSaveRequest(Unique.Code("U"), "Test unit"),
            ["users"] = new UserSaveRequest(account.Username, account.Email, "Renamed", Roles.Clerk, true, null, account.WarehouseIds.ToList()),
            ["approval-rules"] = new ApprovalRuleSaveRequest(DocumentType.Issue, 1000m, null, true, ApproverTier.Standard)
        };

        foreach (var (route, body) in routes)
        {
            var unknown = (await s.Admin.PutAsync($"/api/{route}/{Guid.NewGuid()}", body)).ShouldBeProblem(HttpStatusCode.NotFound);
            var otherKinds = new List<Guid> { product.Id, Guid.Empty };
            if (route != "warehouses") otherKinds.Add(warehouse); // a real warehouse id is the right kind on the warehouse route
            foreach (var other in otherKinds)
            {
                var problem = (await s.Admin.PutAsync($"/api/{route}/{other}", body)).ShouldBeProblem(HttpStatusCode.NotFound);
                Assert.True((unknown.Title, unknown.Detail) == (problem.Title, problem.Detail), $"{route}/{other}: the 404 differs from the one for an unknown id");
            }
        }
    }

    [Fact]
    public async Task A_document_that_names_its_warehouse_or_partner_by_the_id_of_another_kind_is_refused_like_an_unknown_one()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        var line = new Line(product.Id, 1, 1m);
        var warehouse = fixture.Reference.DefaultWarehouseId;

        var unknownWarehouse = (await s.Admin.Documents.CreateReceiptAsync(new DocumentBuilder(Guid.NewGuid()).WithLines(line).Build())).ShouldBeProblem(HttpStatusCode.BadRequest);
        var wrongWarehouse = (await s.Admin.Documents.CreateReceiptAsync(new DocumentBuilder(product.Id).WithLines(line).Build())).ShouldBeProblem(HttpStatusCode.BadRequest);
        var unknownPartner = (await s.Admin.Documents.CreateReceiptAsync(new DocumentBuilder(warehouse).WithPartner(Guid.NewGuid()).WithLines(line).Build())).ShouldBeProblem(HttpStatusCode.BadRequest);
        var wrongPartner = (await s.Admin.Documents.CreateReceiptAsync(new DocumentBuilder(warehouse).WithPartner(product.Id).WithLines(line).Build())).ShouldBeProblem(HttpStatusCode.BadRequest);

        Assert.Equal((unknownWarehouse.Title, unknownWarehouse.Detail), (wrongWarehouse.Title, wrongWarehouse.Detail));
        Assert.Equal((unknownPartner.Title, unknownPartner.Detail), (wrongPartner.Title, wrongPartner.Detail));
    }

    [Theory]
    [InlineData("/api/documents")]
    [InlineData("/api/audit")]
    [InlineData("/api/stock/ledger")]
    public async Task A_real_cursor_with_one_character_changed_is_a_400_on_the_cursor_field_never_a_500_or_a_page(string url)
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        for (var i = 0; i < 3; i++) await s.ReceiveAsync(fixture.Reference.DefaultWarehouseId, new Line(product.Id, 1, 1m));
        var real = (await s.Admin.GetAsync($"{url}?pageSize=1")).Data<PagedResult<System.Text.Json.JsonElement>>().NextCursor!;
        var middle = real.Length / 2;
        var changed = real[..middle] + (real[middle] == 'A' ? 'B' : 'A') + real[(middle + 1)..];

        var problem = (await s.Admin.GetAsync($"{url}?pageSize=1&cursor={Uri.EscapeDataString(changed)}")).ShouldBeProblem(HttpStatusCode.BadRequest);

        Assert.Equal([Messages.CursorInvalid], problem.ErrorsFor("cursor"));
    }
}
