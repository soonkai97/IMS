using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Ims.Api.Tests.Builders;
using Ims.Api.Tests.Harness;
using Ims.Domain;

namespace Ims.Api.Tests.Tests.Contract;

/// <summary>
/// BR-10: instants are stored in UTC. On the wire every one of them says so with a trailing Z, so a browser never mistakes it for local time.
/// The database returns "unspecified" values; the converters in the API and the persistence layer are what make them UTC.
/// </summary>
[Collection(ApiCollection.Name)]
public partial class UtcTimestampTests(ApiFixture fixture)
{
    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}")]
    private static partial Regex InstantStart();

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d{1,7})?Z$")]
    private static partial Regex UtcInstant();

    /// <summary>Walks a JSON value and checks that every string that looks like a date-time is UTC with a Z. Returns how many it saw.</summary>
    private static int AssertEveryInstantIsUtc(JsonElement element, string path = "$")
    {
        var seen = 0;
        switch (element.ValueKind)
        {
            case JsonValueKind.String when InstantStart().IsMatch(element.GetString()!):
                var text = element.GetString()!;
                Assert.True(UtcInstant().IsMatch(text), $"{path} = \"{text}\" is not a UTC instant with a Z.");
                Assert.Equal(DateTimeKind.Utc, DateTime.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).Kind);
                seen++;
                break;
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject()) seen += AssertEveryInstantIsUtc(property.Value, $"{path}.{property.Name}");
                break;
            case JsonValueKind.Array:
                var index = 0;
                foreach (var item in element.EnumerateArray()) seen += AssertEveryInstantIsUtc(item, $"{path}[{index++}]");
                break;
        }
        return seen;
    }

    private static int InstantsIn(ApiResponse response)
    {
        using var json = response.ShouldBeOk().Json();
        return AssertEveryInstantIsUtc(json.RootElement);
    }

    // ----- the converters themselves -----
    [Theory]
    [InlineData(DateTimeKind.Utc)]
    [InlineData(DateTimeKind.Unspecified)]
    public void The_converter_writes_utc_and_unspecified_values_with_a_Z(DateTimeKind kind)
    {
        var value = new DateTime(2026, 10, 9, 14, 7, 35, 40, kind);

        var json = JsonSerializer.Serialize(value, Options());

        Assert.Equal("\"2026-10-09T14:07:35.04Z\"", json);
    }

    [Fact]
    public void The_converter_converts_a_local_value_to_utc()
    {
        var local = new DateTime(2026, 10, 9, 14, 7, 35, DateTimeKind.Local);

        var json = JsonSerializer.Serialize(local, Options());

        Assert.Equal($"\"{local.ToUniversalTime():yyyy-MM-dd'T'HH:mm:ss'Z'}\"", json);
    }

    [Fact]
    public void A_whole_second_has_no_fraction_and_a_null_instant_stays_null()
    {
        Assert.Equal("\"2026-10-09T14:07:35Z\"", JsonSerializer.Serialize(new DateTime(2026, 10, 9, 14, 7, 35, DateTimeKind.Utc), Options()));
        Assert.Equal("null", JsonSerializer.Serialize((DateTime?)null, Options()));
        Assert.Equal("\"2026-10-09T14:07:35Z\"", JsonSerializer.Serialize((DateTime?)new DateTime(2026, 10, 9, 14, 7, 35, DateTimeKind.Unspecified), Options()));
    }

    [Fact]
    public void An_incoming_instant_is_read_as_utc_whatever_zone_it_names()
    {
        var withZ = JsonSerializer.Deserialize<DateTime>("\"2026-10-09T14:07:35Z\"", Options());
        var withOffset = JsonSerializer.Deserialize<DateTime>("\"2026-10-09T22:07:35+08:00\"", Options());
        var bare = JsonSerializer.Deserialize<DateTime>("\"2026-10-09T14:07:35\"", Options());

        var expected = new DateTime(2026, 10, 9, 14, 7, 35, DateTimeKind.Utc);
        foreach (var value in new[] { withZ, withOffset, bare })
        {
            Assert.Equal(DateTimeKind.Utc, value.Kind);
            Assert.Equal(expected, value);
        }
    }

    private static JsonSerializerOptions Options()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        WireJson.Configure(options);
        return options;
    }

    // ----- through the real pipeline -----
    [Fact]
    public async Task A_document_and_its_list_item_have_every_timestamp_in_utc_with_a_Z()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        var posted = await s.ReceiveAsync(fixture.Reference.DefaultWarehouseId, new Line(product.Id, 3, 1m));

        var seenOnDocument = InstantsIn(await s.Admin.Documents.GetAsync(posted.Id));
        var seenInList = InstantsIn(await s.Admin.Documents.ListAsync(search: posted.No));

        Assert.True(seenOnDocument >= 3, $"createdAt, submittedAt and postedAt expected, saw {seenOnDocument}");
        Assert.True(seenInList >= 3);
    }

    [Fact]
    public async Task The_timestamps_of_a_new_document_are_close_to_now()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        var before = DateTime.UtcNow.AddSeconds(-5);

        var created = await s.DraftReceiptAsync(s.Clerk, fixture.Reference.DefaultWarehouseId, new Line(product.Id, 1, 1m));
        var reread = await s.GetAsync(created.Id);

        Assert.InRange(created.CreatedAt, before, DateTime.UtcNow.AddSeconds(5));
        Assert.Equal(created.CreatedAt, reread.CreatedAt);
        Assert.Equal(DateTimeKind.Utc, reread.CreatedAt.Kind);
    }

    [Fact]
    public async Task The_ledger_the_audit_log_and_the_movements_report_use_utc_with_a_Z()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        await s.ReceiveAsync(fixture.Reference.DefaultWarehouseId, new Line(product.Id, 3, 1m));

        Assert.True(InstantsIn(await s.Admin.Stock.LedgerAsync(product.Id)) > 0);
        Assert.True(InstantsIn(await s.Admin.Audit.QueryAsync()) > 0);
        Assert.True(InstantsIn(await s.Admin.Reports.MovementsAsync(product.Id)) > 0);
    }

    [Fact]
    public async Task The_login_response_expiry_is_utc_with_a_Z()
    {
        var account = await new AccountFactory(fixture).CreateAsync(Roles.Clerk);

        var login = await fixture.Anonymous().Auth.LoginAsync(account.Username, account.Password);

        Assert.True(InstantsIn(login) > 0);
    }

    [Fact]
    public async Task A_rejected_document_returns_its_returned_at_in_utc_with_a_Z()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        var submitted = await s.SubmitAsync(s.Clerk, await s.DraftReceiptAsync(s.Clerk, fixture.Reference.DefaultWarehouseId, new Line(product.Id, 1, 1m)));

        var response = await s.Supervisor.Documents.RejectAsync(submitted.Id, "Wrong supplier");

        Assert.True(InstantsIn(response) >= 2); // createdAt and returnedAt
    }
}
