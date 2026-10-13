using System.Buffers.Text;
using System.Net;
using System.Text.Json;
using Ims.Api.Tests.Builders;
using Ims.Api.Tests.Harness;
using Ims.Application;
using Ims.Application.Features.Documents;
using Ims.Domain;

namespace Ims.Api.Tests.Tests.PublicIds;

/// <summary>
/// PID-01 and PID-02 (security retest of the public-id switch, decision D2): nothing on the wire is a counter shared by all warehouses. A user who sees one warehouse must not be able to
/// measure what the others write, neither from the ledger and audit rows nor from the concurrency token of a document.
/// </summary>
[Collection(ApiCollection.Name)]
public class CounterLeakTests(ApiFixture fixture)
{
    private Guid Warehouse => fixture.Reference.DefaultWarehouseId;

    private static IEnumerable<JsonElement> Items(ApiResponse response, out JsonDocument json)
    {
        json = response.ShouldBeOk().Json();
        return json.RootElement.GetProperty("items").EnumerateArray().ToList();
    }

    // ----- PID-01: no row id on ledger and audit rows -----

    [Theory]
    [InlineData(Roles.WarehouseStaff)]
    [InlineData(Roles.Clerk)]
    [InlineData(Roles.Admin)]
    public async Task A_ledger_row_carries_no_id_for_any_role(string role)
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        await s.ReceiveAsync(Warehouse, new Line(product.Id, 3, 1m));

        var rows = Items(await s.As(role).Stock.LedgerAsync(product.Id), out var json).ToList();
        using (json)
        {
            Assert.NotEmpty(rows);
            Assert.All(rows, row => Assert.False(row.TryGetProperty("id", out _), row.GetRawText()));
        }
    }

    [Fact]
    public async Task A_scoped_user_cannot_count_the_ledger_rows_other_warehouses_write()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        var other = await s.NewWarehouseAsync();
        await s.ReceiveAsync(Warehouse, new Line(product.Id, 3, 1m));

        // What the staff user can compare between two reads: the fields of its own rows, before and after the admin writes in another warehouse.
        using var before = (await s.Staff.GetAsync("/api/stock/ledger?pageSize=5")).ShouldBeOk().Json();
        await s.ReceiveAsync(other.Id, new Line(product.Id, 1, 1m), new Line((await s.NewProductAsync()).Id, 1, 1m));
        await s.ReceiveAsync(other.Id, new Line(product.Id, 2, 1m));
        using var after = (await s.Staff.GetAsync("/api/stock/ledger?pageSize=5")).ShouldBeOk().Json();

        var seen = before.RootElement.GetProperty("items").EnumerateArray().Concat(after.RootElement.GetProperty("items").EnumerateArray()).ToList();
        Assert.NotEmpty(seen);
        Assert.All(seen, row => Assert.NotEqual(other.Code, row.GetProperty("warehouseCode").GetString()));   // the other warehouse's rows are not shown...
        Assert.All(seen, row => Assert.DoesNotContain(row.EnumerateObject(), p => p.Value.ValueKind == JsonValueKind.Number && p.Name is not ("qtyDelta" or "unitCost" or "balanceAfter")));   // ...and nothing numeric but quantities and money is
    }

    [Fact]
    public async Task The_ledger_still_pages_by_cursor_without_an_id_in_the_rows()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        for (var i = 1; i <= 5; i++) await s.ReceiveAsync(Warehouse, new Line(product.Id, i, 1m));

        var walked = new List<decimal>();
        string? cursor = null;
        do
        {
            var page = (await s.Staff.GetAsync($"/api/stock/ledger?productId={product.Id}&pageSize=2&includeTotal=false" + (cursor is null ? "" : $"&cursor={Uri.EscapeDataString(cursor)}")))
                .Data<PagedResult<Ims.Application.Features.Stock.LedgerRow>>();
            walked.AddRange(page.Items.Select(r => r.QtyDelta));
            cursor = page.NextCursor;
        } while (cursor is not null);

        Assert.Equal([5m, 4m, 3m, 2m, 1m], walked);   // newest first, each row once
    }

    [Fact]
    public async Task An_audit_row_carries_no_id_and_the_audit_log_still_pages_by_cursor()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        await s.ReceiveAsync(Warehouse, new Line(product.Id, 3, 1m));

        var first = await s.Admin.GetAsync("/api/audit?pageSize=3&includeTotal=false");
        var rows = Items(first, out var json).ToList();
        using (json)
        {
            Assert.Equal(3, rows.Count);
            Assert.All(rows, row => Assert.False(row.TryGetProperty("id", out _), row.GetRawText()));

            var cursor = json.RootElement.GetProperty("nextCursor").GetString()!;
            var next = Items(await s.Admin.GetAsync($"/api/audit?pageSize=3&includeTotal=false&cursor={Uri.EscapeDataString(cursor)}"), out var nextJson).ToList();
            using (nextJson)
            {
                Assert.NotEmpty(next);
                Assert.True(next[0].GetProperty("at").GetDateTime() <= rows[^1].GetProperty("at").GetDateTime());
                Assert.DoesNotContain(next, n => rows.Any(r => r.GetRawText() == n.GetRawText()));   // the next page continues after the first, it does not repeat it
            }
        }
    }

    // ----- PID-02: the concurrency token is not the database-wide row version -----

    private static string TokenOf(ApiResponse response)
    {
        using var json = response.Json();
        return json.RootElement.GetProperty("concurrencyToken").GetString()!;
    }

    [Fact]
    public async Task A_token_is_opaque_and_follows_no_counter_even_while_other_warehouses_write()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        var other = await s.NewWarehouseAsync();

        // Drafts of the staff user in its own warehouse, with the admin posting elsewhere in between (the retest saw 2347 then 2384, a count of the writes in between).
        var tokens = new List<string>();
        for (var i = 0; i < 12; i++)
        {
            tokens.Add(TokenOf(await s.Clerk.Documents.CreateReceiptAsync(new DocumentBuilder(Warehouse).WithPartner(s.Reference.SupplierId).WithLines(new Line(product.Id, 1, 1m)).Build())));
            await s.ReceiveAsync(other.Id, new Line(product.Id, 1, 1m));
        }

        Assert.Equal(tokens.Count, tokens.Distinct().Count());
        Assert.All(tokens, t =>
        {
            Assert.False(long.TryParse(t, out _), $"{t} is a number");
            Assert.True(Base64Url.IsValid(t), $"{t} is not base64url");
            Assert.NotEqual(8, Base64Url.DecodeFromChars(t).Length);   // the raw row version is 8 bytes
        });

        // Read as a big-endian number the way the retest did, the tokens are not a rising sequence: a counter would be (12 values in order by chance: one in 479 million).
        var numbers = tokens.Select(t => BitConverter.ToUInt64(Base64Url.DecodeFromChars(t).Take(8).Reverse().ToArray())).ToList();
        Assert.NotEqual(numbers.Order().ToList(), numbers);
        Assert.NotEqual(numbers.OrderDescending().ToList(), numbers);
    }

    [Fact]
    public async Task A_document_keeps_its_token_whatever_other_warehouses_write_and_changes_it_when_it_changes()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        var other = await s.NewWarehouseAsync();
        var draft = await s.DraftReceiptAsync(s.Clerk, Warehouse, new Line(product.Id, 1, 1m));
        var before = TokenOf(await s.Clerk.Documents.GetAsync(draft.Id));

        await s.ReceiveAsync(other.Id, new Line(product.Id, 4, 1m));

        Assert.Equal(before, TokenOf(await s.Clerk.Documents.GetAsync(draft.Id)));
        var submitted = await s.Clerk.Documents.SubmitAsync(draft.Id, before);
        Assert.NotEqual(before, TokenOf(submitted.ShouldBeOk()));
    }

    [Fact]
    public async Task Two_documents_never_share_a_token_and_a_token_does_not_open_another_document()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        var a = await s.DraftReceiptAsync(s.Clerk, Warehouse, new Line(product.Id, 1, 1m));
        var b = await s.DraftReceiptAsync(s.Clerk, Warehouse, new Line(product.Id, 1, 1m));
        var tokenOfA = TokenOf(await s.Clerk.Documents.GetAsync(a.Id));

        Assert.NotEqual(tokenOfA, TokenOf(await s.Clerk.Documents.GetAsync(b.Id)));
        (await s.Clerk.Documents.SubmitAsync(b.Id, tokenOfA)).ShouldBeProblem(HttpStatusCode.Conflict, "Conflict");
        Assert.Equal(DocumentStatus.Draft, (await s.GetAsync(b.Id)).Status);
    }

    public static TheoryData<string> BadTokens() => new()
    {
        "AAAAAAAACHw=",                                  // the old form: the row version in base64
        "AAAAAAAACHw",
        "00000000000000000000000000",
        new string('A', 22),                             // right shape, not made by the server
        new string('x', 2000),
        "%00",
        "ünï©ode"
    };

    [Theory]
    [MemberData(nameof(BadTokens))]
    public async Task A_forged_or_old_form_token_is_the_same_conflict_as_a_stale_one_never_a_server_error(string forged)
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        var draft = await s.DraftReceiptAsync(s.Clerk, Warehouse, new Line(product.Id, 1, 1m));

        var response = await s.Clerk.Documents.SubmitAsync(draft.Id, forged);

        response.ShouldBeProblem(HttpStatusCode.Conflict, "Conflict");
        Assert.Equal(DocumentStatus.Draft, (await s.GetAsync(draft.Id)).Status);
    }

    [Fact]
    public async Task A_token_with_one_character_changed_is_refused()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        var draft = await s.DraftReceiptAsync(s.Clerk, Warehouse, new Line(product.Id, 1, 1m));
        var token = TokenOf(await s.Clerk.Documents.GetAsync(draft.Id));
        var changed = (token[0] == 'A' ? 'B' : 'A') + token[1..];

        (await s.Clerk.Documents.SubmitAsync(draft.Id, changed)).ShouldBeProblem(HttpStatusCode.Conflict, "Conflict");
        (await s.Clerk.Documents.SubmitAsync(draft.Id, token)).ShouldBeOk();
    }
}
