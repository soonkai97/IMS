using System.Net;
using Ims.Api.Tests.Builders;
using Ims.Api.Tests.Harness;
using Ims.Api.Tests.Support;
using Ims.Application;
using Ims.Application.Features.Partners;
using Ims.Domain;

namespace Ims.Api.Tests.Tests.MasterData;

[Collection(ApiCollection.Name)]
public class PartnerTests(ApiFixture fixture)
{
    private static PartnerSaveRequest Request(PartnerType type, bool isActive = true) =>
        new(type, Unique.Name("Partner"), "0123456789", isActive);

    [Fact]
    public async Task Suppliers_and_customers_can_be_created_and_listed_by_type()
    {
        var admin = await fixture.AsAsync("Admin");
        var supplier = (await admin.Partners.CreateAsync(Request(PartnerType.Supplier))).Data<PartnerDto>();
        var customer = (await admin.Partners.CreateAsync(Request(PartnerType.Customer))).Data<PartnerDto>();

        var suppliers = (await admin.Partners.ListAsync(PartnerType.Supplier, search: supplier.Name)).Data<PagedResult<PartnerDto>>().Items;
        var customers = (await admin.Partners.ListAsync(PartnerType.Customer, search: customer.Name)).Data<PagedResult<PartnerDto>>().Items;

        Assert.Contains(suppliers, p => p.Id == supplier.Id);
        Assert.DoesNotContain(suppliers, p => p.Id == customer.Id);
        Assert.Contains(customers, p => p.Id == customer.Id);
        Assert.Equal("0123456789", supplier.Contact);
    }

    [Fact]
    public async Task Deactivating_hides_the_partner_by_default_and_blocks_new_documents()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var supplier = (await s.Admin.Partners.CreateAsync(Request(PartnerType.Supplier))).Data<PartnerDto>();
        var product = await s.NewProductAsync();
        var request = new DocumentBuilder(fixture.Reference.DefaultWarehouseId).WithPartner(supplier.Id).WithLines(new Line(product.Id, 1, 1m)).Build();
        (await s.Admin.Documents.CreateReceiptAsync(request)).ShouldBeOk();

        (await s.Admin.Partners.UpdateAsync(supplier.Id, new PartnerSaveRequest(supplier.Type, supplier.Name, supplier.Contact, false))).ShouldBeOk();

        Assert.DoesNotContain((await s.Admin.Partners.ListAsync(PartnerType.Supplier, search: supplier.Name)).Data<PagedResult<PartnerDto>>().Items, p => p.Id == supplier.Id);
        Assert.Contains((await s.Admin.Partners.ListAsync(PartnerType.Supplier, includeInactive: true, search: supplier.Name)).Data<PagedResult<PartnerDto>>().Items, p => p.Id == supplier.Id && !p.IsActive);
        Assert.Contains("inactive", (await s.Admin.Documents.CreateReceiptAsync(request)).ShouldBeProblem(HttpStatusCode.BadRequest).Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_receipt_needs_a_supplier_and_an_issue_needs_a_customer()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        var warehouse = fixture.Reference.DefaultWarehouseId;
        var line = new Line(product.Id, 1, 1m);

        var receiptWithCustomer = new DocumentBuilder(warehouse).WithPartner(fixture.Reference.CustomerId).WithLines(line).Build();
        var issueWithSupplier = new DocumentBuilder(warehouse).WithPartner(fixture.Reference.SupplierId).WithLines(line).Build();

        Assert.Contains("supplier", (await s.Admin.Documents.CreateReceiptAsync(receiptWithCustomer)).ShouldBeProblem(HttpStatusCode.BadRequest).Detail);
        Assert.Contains("customer", (await s.Admin.Documents.CreateIssueAsync(issueWithSupplier)).ShouldBeProblem(HttpStatusCode.BadRequest).Detail);
    }

    [Fact]
    public async Task A_partner_that_does_not_exist_is_refused()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        var request = new DocumentBuilder(fixture.Reference.DefaultWarehouseId).WithPartner(9_999_999).WithLines(new Line(product.Id, 1, 1m)).Build();

        (await s.Admin.Documents.CreateReceiptAsync(request)).ShouldBeProblem(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Invalid_partners_are_400_and_a_missing_partner_is_404()
    {
        var admin = await fixture.AsAsync("Admin");

        var noName = await admin.Partners.CreateAsync(new PartnerSaveRequest(PartnerType.Supplier, "", null, true));
        Assert.NotEmpty(noName.ShouldBeProblem(HttpStatusCode.BadRequest).ErrorsFor("Name"));
        (await admin.PostAsync("/api/partners", new { type = 99, name = "Bad type", isActive = true })).ShouldBeProblem(HttpStatusCode.BadRequest);
        (await admin.Partners.UpdateAsync(9_999_999, Request(PartnerType.Supplier))).ShouldBeProblem(HttpStatusCode.NotFound);
    }

    // ---------- who may create, who may change ----------

    [Fact]
    public async Task A_clerk_can_create_a_partner_but_cannot_edit_or_deactivate_it()
    {
        var clerk = await fixture.AsAsync(Roles.Clerk);

        var created = (await clerk.Partners.CreateAsync(Request(PartnerType.Customer))).Data<PartnerDto>();   // this API answers 200 for every create

        Assert.True(created.IsActive);
        Assert.Contains((await clerk.Partners.ListAsync(PartnerType.Customer, search: created.Name)).Data<PagedResult<PartnerDto>>().Items, p => p.Id == created.Id);
        (await clerk.Partners.UpdateAsync(created.Id, new PartnerSaveRequest(created.Type, created.Name + " edited", created.Contact, true))).ShouldBe(HttpStatusCode.Forbidden);
        (await clerk.Partners.UpdateAsync(created.Id, new PartnerSaveRequest(created.Type, created.Name, created.Contact, false))).ShouldBe(HttpStatusCode.Forbidden);

        var stored = (await clerk.Partners.ListAsync(PartnerType.Customer, search: created.Name)).Data<PagedResult<PartnerDto>>().Items.Single(p => p.Id == created.Id);
        Assert.Equal(created, stored);
    }

    [Theory]
    [InlineData(Roles.Owner)]
    [InlineData(Roles.WarehouseStaff)]
    public async Task Owner_and_warehouse_staff_cannot_create_partners(string role)
    {
        var client = await fixture.AsAsync(role);

        (await client.Partners.CreateAsync(Request(PartnerType.Supplier))).ShouldBe(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData(Roles.Supervisor)]
    [InlineData(Roles.Admin)]
    public async Task Supervisor_and_admin_create_edit_and_deactivate_partners(string role)
    {
        var client = await fixture.AsAsync(role);
        var created = (await client.Partners.CreateAsync(Request(PartnerType.Supplier))).Data<PartnerDto>();

        var renamed = (await client.Partners.UpdateAsync(created.Id, new PartnerSaveRequest(created.Type, created.Name + " renamed", "555", true))).Data<PartnerDto>();
        var deactivated = (await client.Partners.UpdateAsync(created.Id, new PartnerSaveRequest(created.Type, renamed.Name, renamed.Contact, false))).Data<PartnerDto>();

        Assert.Equal(created.Name + " renamed", renamed.Name);
        Assert.False(deactivated.IsActive);
    }

    // ---------- duplicates ----------

    [Fact]
    public async Task A_second_partner_with_the_same_name_and_type_is_409_ignoring_case_and_surrounding_spaces()
    {
        var clerk = await fixture.AsAsync(Roles.Clerk);
        var first = (await clerk.Partners.CreateAsync(Request(PartnerType.Supplier))).Data<PartnerDto>();

        foreach (var spelling in new[] { first.Name, first.Name.ToUpperInvariant(), first.Name.ToLowerInvariant(), $"  {first.Name}  " })
        {
            var problem = (await clerk.Partners.CreateAsync(new PartnerSaveRequest(PartnerType.Supplier, spelling, null, true))).ShouldBeProblem(HttpStatusCode.Conflict);
            Assert.Contains("already exists", problem.Detail);
        }

        var all = (await clerk.Partners.ListAsync(PartnerType.Supplier, includeInactive: true, search: first.Name)).Data<PagedResult<PartnerDto>>().Items;
        Assert.Single(all);
    }

    [Fact]
    public async Task The_same_name_is_allowed_for_a_supplier_and_a_customer()
    {
        var admin = await fixture.AsAsync(Roles.Admin);
        var name = Unique.Name("Both");

        (await admin.Partners.CreateAsync(new PartnerSaveRequest(PartnerType.Supplier, name, null, true))).ShouldBeOk();
        (await admin.Partners.CreateAsync(new PartnerSaveRequest(PartnerType.Customer, name, null, true))).ShouldBeOk();
    }

    [Fact]
    public async Task A_deactivated_partner_still_blocks_its_name_so_it_is_reactivated_instead_of_duplicated()
    {
        var admin = await fixture.AsAsync(Roles.Admin);
        var partner = (await admin.Partners.CreateAsync(Request(PartnerType.Supplier))).Data<PartnerDto>();
        (await admin.Partners.UpdateAsync(partner.Id, new PartnerSaveRequest(partner.Type, partner.Name, null, false))).ShouldBeOk();

        (await admin.Partners.CreateAsync(new PartnerSaveRequest(PartnerType.Supplier, partner.Name, null, true))).ShouldBeProblem(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Renaming_a_partner_onto_another_partners_name_is_409_and_keeping_its_own_name_is_fine()
    {
        var admin = await fixture.AsAsync(Roles.Admin);
        var a = (await admin.Partners.CreateAsync(Request(PartnerType.Supplier))).Data<PartnerDto>();
        var b = (await admin.Partners.CreateAsync(Request(PartnerType.Supplier))).Data<PartnerDto>();

        (await admin.Partners.UpdateAsync(b.Id, new PartnerSaveRequest(b.Type, a.Name.ToUpperInvariant(), null, true))).ShouldBeProblem(HttpStatusCode.Conflict);
        (await admin.Partners.UpdateAsync(b.Id, new PartnerSaveRequest(b.Type, b.Name, "new contact", true))).ShouldBeOk();
    }

    [Fact]
    public async Task Simultaneous_creates_of_the_same_partner_leave_exactly_one_row()
    {
        var clerk = await fixture.AsAsync(Roles.Clerk);
        var name = Unique.Name("Race");

        var responses = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => clerk.Partners.CreateAsync(new PartnerSaveRequest(PartnerType.Supplier, name, null, true))));

        Assert.Equal(1, responses.Count(r => r.Status == HttpStatusCode.OK));
        Assert.All(responses.Where(r => r.Status != HttpStatusCode.OK), r => r.ShouldBeProblem(HttpStatusCode.Conflict));
        Assert.Single((await clerk.Partners.ListAsync(PartnerType.Supplier, includeInactive: true, search: name)).Data<PagedResult<PartnerDto>>().Items);
    }
}
