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
}
