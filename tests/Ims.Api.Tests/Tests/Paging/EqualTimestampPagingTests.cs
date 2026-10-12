using Ims.Api.Tests.Builders;
using Ims.Api.Tests.Clients;
using Ims.Api.Tests.Harness;
using Ims.Api.Tests.Support;
using Ims.Application;
using Ims.Application.Features.Documents;
using Ims.Domain;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Ims.Api.Tests.Tests.Paging;

/// <summary>
/// Walks the documents, the ledger and the audit log page by page when every row has the same timestamp (the host that writes them has a clock
/// that never moves), and again while rows are added between two pages. Each row must be seen once: none skipped, none repeated.
/// A row is identified by a business value (document number, quantity, audit entity id), never by the row id, so the tests hold when ids change.
/// </summary>
[Collection(ApiCollection.Name)]
public class EqualTimestampPagingTests(ApiFixture fixture)
{
    private const int FirstRows = 5;
    private const int AddedBetweenPages = 2;
    private const decimal FirstQuantity = 1m;
    private const decimal AddedQuantity = 100m;

    private sealed record Row(string Key, DateTime Stamp);

    /// <summary>The text that stands for a whole quantity, whatever scale the API gives it.</summary>
    private static string QuantityKey(decimal quantity) => decimal.ToInt32(quantity).ToString();

    private WebApplicationFactory<Program> FrozenHost() =>
        fixture.CreateHost(b => b.ConfigureTestServices(s => s.AddSingleton<IClock>(new FrozenClock())));

    private static ApiClient SignedIn(WebApplicationFactory<Program> host, string accessToken)
    {
        var client = ApiFixture.CreateClient(host);
        client.AccessToken = accessToken;
        return client;
    }

    private async Task<ApiClient> AdminOn(WebApplicationFactory<Program> host) =>
        SignedIn(host, (await fixture.AsAsync(Roles.Admin)).AccessToken!);

    private DocumentSaveRequest Receipt(Guid warehouseId, int productId, decimal quantity) =>
        new DocumentBuilder(warehouseId).WithPartner(fixture.Reference.SupplierId).WithLines(new Line(productId, quantity, 1m)).Build();

    private async Task<DocumentDto> PostReceiptAsync(ApiClient by, Guid warehouseId, int productId, decimal quantity)
    {
        var draft = (await by.Documents.CreateReceiptAsync(Receipt(warehouseId, productId, quantity))).Data<DocumentDto>();
        (await by.Documents.SubmitAsync(draft.Id)).ShouldBeOk();
        return (await by.Documents.PostAsync(draft.Id)).Data<DocumentDto>();
    }

    private static void AssertEachRowSeenOnce(IEnumerable<string> first, IEnumerable<string> added, IReadOnlyList<Row> walked)
    {
        var keys = walked.Select(r => r.Key).ToList();
        var repeated = keys.GroupBy(k => k).Where(g => g.Count() > 1).Select(g => g.Key).ToList();

        Assert.True(repeated.Count == 0, $"Rows seen twice: {string.Join(", ", repeated)}");
        Assert.True(!first.Except(keys).Any(), $"Rows skipped: {string.Join(", ", first.Except(keys))}");
        Assert.True(!keys.Except(first.Concat(added)).Any(), $"Rows from elsewhere: {string.Join(", ", keys.Except(first.Concat(added)))}");
        Assert.Single(walked.Select(r => r.Stamp).Distinct()); // the control: the timestamps really were equal
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public async Task Documents_with_the_same_creation_time_are_each_listed_once_even_when_more_are_added_between_pages(int pageSize)
    {
        var s = await StockScenario.CreateAsync(fixture);
        var warehouse = await s.NewWarehouseAsync();
        var product = await s.NewProductAsync();
        using var host = FrozenHost();
        var admin = await AdminOn(host);
        async Task<string> DraftAsync() =>
            (await admin.Documents.CreateReceiptAsync(Receipt(warehouse.Id, product.Id, FirstQuantity))).Data<DocumentDto>().No;
        var first = new List<string>();
        for (var i = 0; i < FirstRows; i++) first.Add(await DraftAsync());
        var added = new List<string>();

        var walked = await PageWalk.ReadAsync(s.Admin, $"/api/documents?warehouseId={warehouse.Id}&pageSize={pageSize}&includeTotal=false",
            d => new Row(d.GetProperty("no").GetString()!, d.GetProperty("createdAt").GetDateTime()),
            async page =>
            {
                if (page == 1)
                    for (var i = 0; i < AddedBetweenPages; i++) added.Add(await DraftAsync());
            });

        Assert.Equal(AddedBetweenPages, added.Count);
        AssertEachRowSeenOnce(first, added, walked);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public async Task Ledger_rows_with_the_same_time_are_each_listed_once_even_when_more_are_posted_between_pages(int pageSize)
    {
        var s = await StockScenario.CreateAsync(fixture);
        var warehouse = await s.NewWarehouseAsync();
        var product = await s.NewProductAsync();
        using var host = FrozenHost();
        var admin = await AdminOn(host);
        // Every receipt has its own quantity, so a ledger row is recognised by what it moved.
        var first = new List<string>();
        for (var i = 0; i < FirstRows; i++)
        {
            var quantity = FirstQuantity + i;
            await PostReceiptAsync(admin, warehouse.Id, product.Id, quantity);
            first.Add(QuantityKey(quantity));
        }
        var added = new List<string>();

        var walked = await PageWalk.ReadAsync(s.Admin, $"/api/stock/ledger?productId={product.Id}&pageSize={pageSize}&includeTotal=false",
            r => new Row(QuantityKey(r.GetProperty("qtyDelta").GetDecimal()),r.GetProperty("occurredAt").GetDateTime()),
            async page =>
            {
                if (page != 1) return;
                for (var i = 0; i < AddedBetweenPages; i++)
                {
                    var quantity = AddedQuantity + i;
                    await PostReceiptAsync(admin, warehouse.Id, product.Id, quantity);
                    added.Add(QuantityKey(quantity));
                }
            });

        Assert.Equal(AddedBetweenPages, added.Count);
        AssertEachRowSeenOnce(first, added, walked);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public async Task Audit_entries_with_the_same_time_are_each_listed_once_even_when_more_are_written_between_pages(int pageSize)
    {
        var accounts = new AccountFactory(fixture);
        var account = await accounts.CreateAsync(Roles.Admin);
        var (signedIn, _) = await accounts.SignInAsync(account);
        using var host = FrozenHost();
        var actor = SignedIn(host, signedIn.AccessToken!);
        async Task<string> NewProductAsync() => (await new ProductBuilder(fixture.Reference).CreateAsync(actor)).Id.ToString();
        var first = new List<string>();
        for (var i = 0; i < FirstRows; i++) first.Add(await NewProductAsync());
        var added = new List<string>();

        var walked = await PageWalk.ReadAsync(signedIn,
            $"/api/audit?actor={Uri.EscapeDataString(account.Username)}&entityType=Product&pageSize={pageSize}&includeTotal=false",
            e => new Row(e.GetProperty("entityId").GetString()!, e.GetProperty("at").GetDateTime()),
            async page =>
            {
                if (page == 1)
                    for (var i = 0; i < AddedBetweenPages; i++) added.Add(await NewProductAsync());
            });

        Assert.Equal(AddedBetweenPages, added.Count);
        AssertEachRowSeenOnce(first, added, walked);
    }
}
