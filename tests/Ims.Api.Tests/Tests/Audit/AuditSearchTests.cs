using Ims.Api.Tests.Builders;
using Ims.Api.Tests.Clients;
using Ims.Api.Tests.Harness;
using Ims.Api.Tests.Support;
using Ims.Application.Features.Audit;
using Ims.Domain;

namespace Ims.Api.Tests.Tests.Audit;

/// <summary>
/// PS-06: the audit log can be searched the way people think: a fragment of the object name, a document number, the start of a user name or an
/// action. Every filter is an exact match, a prefix or a short resolved list, so it stays an index seek on a table with millions of rows.
/// </summary>
[Collection(ApiCollection.Name)]
public class AuditSearchTests(ApiFixture fixture)
{
    private int Warehouse => fixture.Reference.DefaultWarehouseId;

    private async Task<List<AuditEntry>> QueryAsync(string? actor = null, string? entityType = null, string? entityId = null, string? action = null, string? search = null)
    {
        var admin = await fixture.AsAsync(Roles.Admin);
        return (await admin.Audit.QueryAsync(actor, entityType, 200, entityId, action, search)).Data<AuditPage>().Items.ToList();
    }

    private async Task<DocumentDtoLite> PostedReceiptAsync()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        var posted = await s.ReceiveAsync(Warehouse, new Line(product.Id, 2, 1m));
        return new DocumentDtoLite(posted.No, product.Sku);
    }

    private sealed record DocumentDtoLite(string No, string Sku);

    [Theory]
    [InlineData("StockDocument")]
    [InlineData("stockdocument")]
    [InlineData("Document")]
    [InlineData("doc")]
    [InlineData("DOCUMENT")]
    public async Task The_object_filter_matches_part_of_the_entity_name_in_any_case(string text)
    {
        var receipt = await PostedReceiptAsync();

        var entries = await QueryAsync(entityType: text);

        Assert.Contains(entries, e => e.EntityId == receipt.No);
        Assert.All(entries, e => Assert.Equal(nameof(StockDocument), e.EntityType));
    }

    [Fact]
    public async Task A_fragment_that_fits_two_entity_names_returns_both()
    {
        await PostedReceiptAsync(); // creates a product and its barcodes rows

        var entries = await QueryAsync(entityType: "product");

        Assert.All(entries, e => Assert.Contains("Product", e.EntityType));
        Assert.Contains(entries, e => e.EntityType == nameof(Product));
    }

    [Fact]
    public async Task A_fragment_that_matches_nothing_returns_nothing_not_everything()
    {
        await PostedReceiptAsync();

        Assert.Empty(await QueryAsync(entityType: "no-such-thing"));
    }

    [Fact]
    public async Task A_document_number_finds_the_history_of_that_document()
    {
        var receipt = await PostedReceiptAsync();

        var byId = await QueryAsync(entityId: receipt.No);
        var bySearch = await QueryAsync(search: receipt.No.ToLowerInvariant());

        Assert.NotEmpty(byId);
        Assert.All(byId, e => Assert.Equal(receipt.No, e.EntityId));
        Assert.Contains(byId, e => e.Action == AuditActions.DocumentPosted);
        Assert.Equal(byId.Select(e => e.Id), bySearch.Where(e => e.EntityId == receipt.No).Select(e => e.Id));
    }

    [Fact]
    public async Task The_start_of_a_document_number_finds_every_document_with_that_prefix()
    {
        var receipt = await PostedReceiptAsync();
        var prefix = receipt.No[..^2];

        var entries = await QueryAsync(entityId: prefix);

        Assert.Contains(entries, e => e.EntityId == receipt.No);
        Assert.All(entries, e => Assert.StartsWith(prefix, e.EntityId, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task The_search_box_accepts_either_an_object_name_or_an_id()
    {
        var receipt = await PostedReceiptAsync();

        var byName = await QueryAsync(search: "Document");
        var byNumber = await QueryAsync(search: receipt.No);

        Assert.Contains(byName, e => e.EntityId == receipt.No);
        Assert.Contains(byNumber, e => e.EntityId == receipt.No);
    }

    [Fact]
    public async Task The_user_filter_matches_the_start_of_a_user_name()
    {
        var accounts = new AccountFactory(fixture);
        var account = await accounts.CreateAsync(Roles.Clerk);
        await accounts.SignInAsync(account);

        var entries = await QueryAsync(actor: account.Username[..^3].ToUpperInvariant());

        Assert.Contains(entries, e => e.Actor == account.Username && e.Action == AuditActions.LoginSuccess);
        Assert.All(entries, e => Assert.StartsWith(account.Username[..^3], e.Actor, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task The_action_filter_matches_the_start_of_an_action_so_logins_can_be_hidden_or_found()
    {
        await PostedReceiptAsync();

        var documentEvents = await QueryAsync(action: "document.");
        var logins = await QueryAsync(action: "login");

        Assert.NotEmpty(documentEvents);
        Assert.All(documentEvents, e => Assert.StartsWith("document.", e.Action));
        Assert.NotEmpty(logins);
        Assert.All(logins, e => Assert.StartsWith("login", e.Action));
    }

    [Fact]
    public async Task Filters_combine()
    {
        var receipt = await PostedReceiptAsync();

        var entries = await QueryAsync(entityType: "document", entityId: receipt.No, action: "document.posted");

        var posted = Assert.Single(entries);
        Assert.Equal((receipt.No, AuditActions.DocumentPosted), (posted.EntityId, posted.Action));
    }

    [Fact]
    public async Task The_filter_options_list_the_entity_names_and_actions_for_a_dropdown()
    {
        var admin = await fixture.AsAsync(Roles.Admin);

        var options = (await admin.Audit.FiltersAsync()).Data<AuditFilterOptions>();

        Assert.Contains(nameof(StockDocument), options.EntityTypes);
        Assert.Contains(nameof(SystemSetting), options.EntityTypes);
        Assert.Contains(nameof(ProductBarcode), options.EntityTypes);
        Assert.Contains(AuditActions.DocumentPosted, options.Actions);
        Assert.Contains(AuditActions.LoginSuccess, options.Actions);
    }

    [Theory]
    [InlineData(Roles.Supervisor)]
    [InlineData(Roles.WarehouseStaff)]
    [InlineData(Roles.Clerk)]
    public async Task The_filter_options_need_the_audit_right(string role)
    {
        var who = await fixture.AsAsync(role);

        (await who.Audit.FiltersAsync()).ShouldBe(System.Net.HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Details_show_enums_by_name()
    {
        var receipt = await PostedReceiptAsync();

        var posted = (await QueryAsync(entityId: receipt.No, action: AuditActions.DocumentPosted)).Single();

        Assert.Contains("\"Status\":\"Submitted\"", posted.Before);
        Assert.Contains("\"Status\":\"Posted\"", posted.After);
        Assert.DoesNotMatch("\"Status\":\\d", posted.After);
    }
}
