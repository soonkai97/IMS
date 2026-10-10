using System.Net;
using Ims.Api.Tests.Builders;
using Ims.Api.Tests.Harness;
using Ims.Api.Tests.Support;
using Ims.Application.Features.Catalog;
using Ims.Application.Features.Documents;
using Ims.Application.Features.Users;
using Ims.Application.Features.Warehouses;
using Ims.Domain;

namespace Ims.Api.Tests.Tests.Contract;

/// <summary>
/// QA findings EA-16, EA-17, EA-18, EA-30 and PS-15: silly input is a 400 with a sentence that names the problem and the limit, never a 500, and a
/// message about a name being "required" is only used when the name is empty.
/// </summary>
[Collection(ApiCollection.Name)]
public class InputValidationTests(ApiFixture fixture)
{
    private int Warehouse => fixture.Reference.DefaultWarehouseId;

    // ----- search boxes (EA-16) -----
    [Theory]
    [InlineData("/api/products?search=")]
    [InlineData("/api/stock?search=")]
    [InlineData("/api/partners?search=")]
    [InlineData("/api/users?search=")]
    [InlineData("/api/documents?search=")]
    [InlineData("/api/audit?search=")]
    [InlineData("/api/reports/stock-summary/export?search=")]
    public async Task A_search_longer_than_the_limit_is_400_not_500(string url)
    {
        var admin = await fixture.AsAsync(Roles.Admin);

        var tooLong = await admin.GetAsync(url + new string('a', Limits.SearchMaxLength + 1));
        var atLimit = await admin.GetAsync(url + new string('a', Limits.SearchMaxLength));
        var huge = await admin.GetAsync(url + new string('a', 4100));

        Assert.Equal([Messages.SearchTooLong], tooLong.ShouldBeProblem(HttpStatusCode.BadRequest).ErrorsFor("search"));
        atLimit.ShouldBeOk();
        huge.ShouldBeProblem(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_barcode_lookup_longer_than_any_code_is_400()
    {
        var admin = await fixture.AsAsync(Roles.Admin);

        (await admin.Products.LookupAsync(new string('9', Limits.BarcodeMaxLength + 1))).ShouldBeProblem(HttpStatusCode.BadRequest);
        (await admin.Products.LookupAsync(new string('9', Limits.BarcodeMaxLength))).ShouldBeOk();
    }

    // ----- date ranges (PS-15) -----
    [Theory]
    [InlineData("/api/documents")]
    [InlineData("/api/stock/ledger")]
    [InlineData("/api/audit")]
    [InlineData("/api/reports/movements")]
    [InlineData("/api/reports/movements/export")]
    [InlineData("/api/reports/ledger/export")]
    public async Task A_from_date_after_the_to_date_is_400_with_a_sentence(string url)
    {
        var admin = await fixture.AsAsync(Roles.Admin);

        var backwards = await admin.GetAsync($"{url}?from=2026-10-10&to=2026-10-01");
        var sameDay = await admin.GetAsync($"{url}?from=2026-10-10&to=2026-10-10");

        Assert.Equal([Messages.DateRangeInvalid], backwards.ShouldBeProblem(HttpStatusCode.BadRequest).ErrorsFor("to"));
        sameDay.ShouldBeOk();
    }

    // ----- user warehouses (EA-17) -----
    [Fact]
    public async Task A_user_cannot_be_assigned_a_warehouse_that_does_not_exist()
    {
        var admin = await fixture.AsAsync(Roles.Admin);
        var username = Unique.Username("wh");
        var request = new UserSaveRequest(username, Unique.Email(username), username, Roles.WarehouseStaff, true, $"Pw-{Guid.NewGuid():N}-1!", [Warehouse, 999_999]);

        var created = await admin.Users.CreateAsync(request);

        Assert.Equal(Messages.WarehouseMissing, created.ShouldBeProblem(HttpStatusCode.BadRequest).Detail);
        var fine = (await admin.Users.CreateAsync(request with { WarehouseIds = [Warehouse] })).Data<UserDto>();
        var update = await admin.Users.UpdateAsync(fine.Id, request with { Password = null, WarehouseIds = [999_999] });
        Assert.Equal(Messages.WarehouseMissing, update.ShouldBeProblem(HttpStatusCode.BadRequest).Detail);
    }

    // ----- user names (EA-30) -----
    [Theory]
    [InlineData("qa u9")]
    [InlineData("name‮with-rtl")]
    [InlineData("emoji\U0001F600")]
    [InlineData("semi;colon")]
    public async Task A_user_name_with_spaces_or_odd_characters_is_refused(string username)
    {
        var admin = await fixture.AsAsync(Roles.Admin);
        var request = new UserSaveRequest(username, Unique.Email(Unique.Username("x")), "Odd name", Roles.Clerk, true, $"Pw-{Guid.NewGuid():N}-1!", [Warehouse]);

        var response = await admin.Users.CreateAsync(request);

        Assert.Contains(Messages.UsernameCharacters, response.ShouldBeProblem(HttpStatusCode.BadRequest).ErrorsFor("username"));
    }

    [Fact]
    public async Task An_email_style_user_name_is_fine()
    {
        var admin = await fixture.AsAsync(Roles.Admin);
        var name = $"first.last+{Guid.NewGuid():N}@example.com";
        var request = new UserSaveRequest(name, Unique.Email(Unique.Username("x")), "Email style", Roles.Clerk, true, $"Pw-{Guid.NewGuid():N}-1!", [Warehouse]);

        (await admin.Users.CreateAsync(request)).ShouldBeOk();
    }

    // ----- documents (EA-30) -----
    [Fact]
    public async Task An_issue_without_a_purpose_is_refused_on_create_and_on_edit()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        var noPurpose = new DocumentSaveRequest(Warehouse, fixture.Reference.CustomerId, null, null, [new DocumentLineInput(product.Id, 1, null, null, null)]);

        var created = await s.Clerk.Documents.CreateIssueAsync(noPurpose);

        Assert.Contains(Messages.PurposeRequired, created.ShouldBeProblem(HttpStatusCode.BadRequest).ErrorsFor("purpose"));
        var draft = await s.DraftIssueAsync(s.Clerk, Warehouse, new Line(product.Id, 1));
        Assert.Equal(IssuePurpose.Sale, draft.Purpose);
        var edited = await s.Clerk.Documents.UpdateAsync(draft.Id, noPurpose);
        Assert.Equal(Messages.PurposeRequired, edited.ShouldBeProblem(HttpStatusCode.BadRequest).Detail);
        Assert.Equal(IssuePurpose.Sale, (await s.GetAsync(draft.Id)).Purpose);
    }

    [Fact]
    public async Task A_receipt_needs_no_purpose()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        var receipt = new DocumentSaveRequest(Warehouse, fixture.Reference.SupplierId, null, null, [new DocumentLineInput(product.Id, 1, null, 1m, null)]);

        (await s.Clerk.Documents.CreateReceiptAsync(receipt)).ShouldBeOk();
    }

    // ----- names: the sentence matches the problem (EA-18) -----
    [Fact]
    public async Task A_too_long_product_name_says_too_long_and_an_empty_one_says_required()
    {
        var admin = await fixture.AsAsync(Roles.Admin);
        var good = new ProductBuilder(fixture.Reference).Build();

        var tooLong = await admin.Products.CreateAsync(good with { Name = new string('n', Limits.NameMaxLength + 1) });
        var empty = await admin.Products.CreateAsync(good with { Name = "  " });

        Assert.Equal([Messages.TextTooLong("Product name", Limits.NameMaxLength)], tooLong.ShouldBeProblem(HttpStatusCode.BadRequest).ErrorsFor("name"));
        Assert.Equal([Messages.ProductNameRequired], empty.ShouldBeProblem(HttpStatusCode.BadRequest).ErrorsFor("name"));
    }

    [Fact]
    public async Task Too_long_names_of_other_master_data_say_how_long_they_may_be()
    {
        var admin = await fixture.AsAsync(Roles.Admin);

        var partner = await admin.Partners.CreateAsync(new(PartnerType.Supplier, new string('p', Limits.NameMaxLength + 1), null, true));
        var warehouse = await admin.Warehouses.CreateAsync(new WarehouseSaveRequest(Unique.Code("W"), new string('w', Limits.ShortNameMaxLength + 1), null, null, true));
        var category = await admin.Categories.CreateAsync(new string('c', Limits.ShortNameMaxLength + 1));

        Assert.Equal([Messages.TextTooLong("Name", Limits.NameMaxLength)], partner.ShouldBeProblem(HttpStatusCode.BadRequest).ErrorsFor("name"));
        Assert.Equal([Messages.TextTooLong("Warehouse name", Limits.ShortNameMaxLength)], warehouse.ShouldBeProblem(HttpStatusCode.BadRequest).ErrorsFor("name"));
        Assert.Equal([Messages.TextTooLong("Category name", Limits.ShortNameMaxLength)], category.ShouldBeProblem(HttpStatusCode.BadRequest).ErrorsFor("name"));
    }
}
