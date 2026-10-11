using Ims.Api.Tests.Builders;
using Ims.Api.Tests.Clients;
using Ims.Api.Tests.Harness;
using Ims.Api.Tests.Support;
using Ims.Application;
using Ims.Application.Features.Documents;
using Ims.Application.Features.Partners;
using Ims.Domain;

namespace Ims.Api.Tests.Tests.Documents;

/// <summary>PR-05: the search box of the receipts and issues lists finds a document by its number, its remark or its partner, as the placeholder promises.</summary>
[Collection(ApiCollection.Name)]
public class DocumentSearchTests(ApiFixture fixture)
{
    private int Warehouse => fixture.Reference.DefaultWarehouseId;

    private static async Task<List<DocumentListItem>> FoundAsync(ApiClient who, string search, string extra = "") =>
        (await who.GetAsync($"/api/documents?search={Uri.EscapeDataString(search)}{extra}&pageSize=100")).Data<PagedResult<DocumentListItem>>().Items.ToList();

    private async Task<DocumentDto> DraftWithRemarkAsync(StockScenario s, ApiClient by, int productId, string remark, int? partnerId = null, DocumentType type = DocumentType.Receipt)
    {
        var request = new DocumentBuilder(Warehouse).WithPartner(partnerId ?? (type == DocumentType.Receipt ? s.Reference.SupplierId : s.Reference.CustomerId)).WithRemark(remark)
            .WithLines(new Line(productId, 1, type == DocumentType.Receipt ? 1m : null)).Build();
        return type == DocumentType.Receipt
            ? (await by.Documents.CreateReceiptAsync(request)).Data<DocumentDto>()
            : (await by.Documents.CreateIssueAsync(request)).Data<DocumentDto>();
    }

    [Fact]
    public async Task A_remark_is_found_by_any_part_of_it_and_regardless_of_case()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        var marker = Unique.Name("remarkmark").ToLowerInvariant();
        var draft = await DraftWithRemarkAsync(s, s.Admin, product.Id, $"Big delivery {marker} from the north dock");

        Assert.Equal(draft.Id, Assert.Single(await FoundAsync(s.Admin, marker)).Id);
        Assert.Equal(draft.Id, Assert.Single(await FoundAsync(s.Admin, marker.ToUpperInvariant())).Id);
        Assert.Equal(draft.Id, Assert.Single(await FoundAsync(s.Admin, $"big delivery {marker}")).Id);
        Assert.Empty(await FoundAsync(s.Admin, marker + "zzz"));
    }

    [Fact]
    public async Task A_partner_is_found_by_any_part_of_its_name()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        var marker = Unique.Name("Partnerfind");
        var supplier = (await s.Admin.Partners.CreateAsync(new PartnerSaveRequest(PartnerType.Supplier, $"{marker} Trading", null, true))).Data<PartnerDto>();
        var other = await DraftWithRemarkAsync(s, s.Admin, product.Id, "somewhere else");
        var draft = await DraftWithRemarkAsync(s, s.Admin, product.Id, "no marker in this remark", supplier.Id);

        var byName = await FoundAsync(s.Admin, marker);
        var byPart = await FoundAsync(s.Admin, marker.ToLowerInvariant()[2..]);

        Assert.Equal(draft.Id, Assert.Single(byName).Id);
        Assert.Equal(draft.Id, Assert.Single(byPart).Id);
        Assert.DoesNotContain(byName, d => d.Id == other.Id);
    }

    [Fact]
    public async Task The_number_still_works_whole_or_in_part()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        var draft = await DraftWithRemarkAsync(s, s.Admin, product.Id, "plain");

        Assert.Contains(await FoundAsync(s.Admin, draft.No), d => d.Id == draft.Id);
        Assert.Contains(await FoundAsync(s.Admin, draft.No[^4..]), d => d.Id == draft.Id); // 0100 finds RCV-WH01-2610-0100
    }

    [Fact]
    public async Task The_search_combines_with_the_other_filters_and_the_total_matches()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        var marker = Unique.Name("combo").ToLowerInvariant();
        var receipt = await DraftWithRemarkAsync(s, s.Admin, product.Id, $"{marker} receipt");
        var issue = await DraftWithRemarkAsync(s, s.Admin, product.Id, $"{marker} issue", type: DocumentType.Issue);

        var all = (await s.Admin.GetAsync($"/api/documents?search={marker}")).Data<PagedResult<DocumentListItem>>();
        var receipts = (await s.Admin.GetAsync($"/api/documents?search={marker}&type=Receipt")).Data<PagedResult<DocumentListItem>>();

        Assert.Equal(2, all.Total);
        Assert.Equal(2, all.Items.Count);
        Assert.Equal((1, receipt.Id), (receipts.Total, Assert.Single(receipts.Items).Id));
        Assert.Contains(all.Items, d => d.Id == issue.Id);
    }

    [Fact]
    public async Task A_user_finds_the_remarks_and_partners_of_their_own_warehouses_only()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var other = await s.NewWarehouseAsync();
        var product = await s.NewProductAsync();
        var marker = Unique.Name("scoped").ToLowerInvariant();
        var request = new DocumentBuilder(other.Id).WithPartner(s.Reference.SupplierId).WithRemark($"{marker} in the other warehouse").WithLines(new Line(product.Id, 1, 1m)).Build();
        var hidden = (await s.Admin.Documents.CreateReceiptAsync(request)).Data<DocumentDto>();

        Assert.Contains(await FoundAsync(s.Admin, marker), d => d.Id == hidden.Id);
        Assert.Empty(await FoundAsync(s.Staff, marker));
    }

    [Fact]
    public async Task Characters_that_mean_something_to_a_search_are_just_text()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        var marker = Unique.Name("wild").ToLowerInvariant();
        var literal = await DraftWithRemarkAsync(s, s.Admin, product.Id, $"{marker} 100% cotton_blend [x]");
        await DraftWithRemarkAsync(s, s.Admin, product.Id, $"{marker} 100 cottonXblend"); // what an unescaped % or _ would also match

        Assert.Equal(literal.Id, Assert.Single(await FoundAsync(s.Admin, $"{marker} 100% cotton_blend")).Id);
        Assert.Equal(literal.Id, Assert.Single(await FoundAsync(s.Admin, "% cotton_blend [x]")).Id);
        Assert.Empty(await FoundAsync(s.Admin, marker + "'; DROP TABLE StockDocument; --"));
    }
}
