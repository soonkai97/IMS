using System.Net;
using Ims.Api.Tests.Builders;
using Ims.Api.Tests.Clients;
using Ims.Api.Tests.Harness;
using Ims.Api.Tests.Support;
using Ims.Domain;

namespace Ims.Api.Tests.Tests.Contract;

/// <summary>Every error the API returns has the same shape (RFC 7807 plus correlationId), and X-Correlation-Id is echoed.</summary>
[Collection(ApiCollection.Name)]
public class ProblemDetailsTests(ApiFixture fixture)
{
    private Guid Warehouse => fixture.Reference.DefaultWarehouseId;

    private static void AssertShape(ApiResponse response, HttpStatusCode status, string title)
    {
        var problem = response.ShouldBeProblem(status, title);
        Assert.Equal("application/problem+json", response.ContentType);
        Assert.False(string.IsNullOrWhiteSpace(problem.Detail), "detail must tell the user what happened");
    }

    [Fact]
    public async Task A_400_from_validation_has_the_shape_and_field_errors()
    {
        var admin = await fixture.AsAsync(Roles.Admin);

        var response = await admin.Products.CreateAsync(new ProductBuilder(fixture.Reference).WithSku("").Build());

        AssertShape(response, HttpStatusCode.BadRequest, "Request rejected");
        Assert.NotEmpty(response.Problem.Errors!);
    }

    [Fact]
    public async Task A_400_from_a_business_rule_has_the_shape()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        var draft = await s.DraftReceiptAsync(s.Admin, Warehouse, new Line(product.Id, 1, 1m));

        AssertShape(await s.Admin.Documents.PostAsync(draft.Id), HttpStatusCode.BadRequest, "Request rejected");
    }

    [Fact]
    public async Task A_400_from_an_unreadable_body_has_the_same_shape()
    {
        var admin = await fixture.AsAsync(Roles.Admin);

        var response = await admin.PostRawJsonAsync("/api/products", "{ not json");

        response.ShouldBe(HttpStatusCode.BadRequest);
        var problem = response.Problem;
        Assert.Equal(400, problem.Status);
        Assert.False(string.IsNullOrWhiteSpace(problem.CorrelationId), $"No correlationId in the body: {response.Body}");
    }

    [Fact]
    public async Task A_400_from_a_value_of_the_wrong_type_has_the_same_shape()
    {
        var admin = await fixture.AsAsync(Roles.Admin);

        var response = await admin.PostAsync("/api/categories", new { name = 123 });

        response.ShouldBe(HttpStatusCode.BadRequest);
        Assert.False(string.IsNullOrWhiteSpace(response.Problem.CorrelationId), $"No correlationId in the body: {response.Body}");
    }

    [Fact]
    public async Task A_401_for_wrong_credentials_has_the_shape()
    {
        var account = await new AccountFactory(fixture).CreateAsync(Roles.Clerk);

        var response = await fixture.Anonymous().Auth.LoginAsync(account.Username, "wrong");

        AssertShape(response, HttpStatusCode.Unauthorized, "Unauthorized");
    }

    [Fact]
    public async Task A_401_for_a_missing_or_bad_token_has_the_shape()
    {
        var missing = await fixture.Anonymous().Warehouses.ListAsync();
        var bad = await fixture.CreateClient("garbage").Warehouses.ListAsync();

        missing.ShouldBe(HttpStatusCode.Unauthorized);
        bad.ShouldBe(HttpStatusCode.Unauthorized);
        Assert.False(string.IsNullOrWhiteSpace(missing.Problem.CorrelationId), $"401 without a token has no problem body: '{missing.Body}'");
        Assert.False(string.IsNullOrWhiteSpace(bad.Problem.CorrelationId), $"401 with a bad token has no problem body: '{bad.Body}'");
        Assert.NotNull(missing.Header("WWW-Authenticate"));
    }

    [Fact]
    public async Task A_403_for_a_missing_permission_has_the_shape()
    {
        var clerk = await fixture.AsAsync(Roles.Clerk);

        var response = await clerk.Settings.UpdateAsync(new(false, false, 24));

        response.ShouldBe(HttpStatusCode.Forbidden);
        Assert.False(string.IsNullOrWhiteSpace(response.Problem.CorrelationId), $"403 from the permission check has no problem body: '{response.Body}'");
    }

    [Fact]
    public async Task A_403_raised_by_a_use_case_has_the_shape()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        var draft = await s.DraftReceiptAsync(s.Admin, fixture.Reference.DefaultWarehouseId, new Line(product.Id, 1, 1m));

        // Warehouse staff may cancel only their own drafts.
        AssertShape(await s.Staff.Documents.CancelAsync(draft.Id, "not mine", draft.ConcurrencyToken), HttpStatusCode.Forbidden, "Forbidden");
    }

    [Fact]
    public async Task A_document_of_another_warehouse_is_a_404_not_a_403()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var other = await s.NewWarehouseAsync();
        var product = await s.NewProductAsync();
        var draft = await s.DraftReceiptAsync(s.Admin, other.Id, new Line(product.Id, 1, 1m));

        AssertShape(await s.Staff.Documents.GetAsync(draft.Id), HttpStatusCode.NotFound, "Not found");
    }

    [Fact]
    public async Task A_404_has_the_shape()
    {
        var admin = await fixture.AsAsync(Roles.Admin);

        AssertShape(await admin.Documents.GetAsync(9_999_999), HttpStatusCode.NotFound, "Not found");
        AssertShape(await admin.Products.GetAsync(9_999_999), HttpStatusCode.NotFound, "Not found");
    }

    [Fact]
    public async Task A_409_for_a_duplicate_has_the_shape()
    {
        var admin = await fixture.AsAsync(Roles.Admin);
        var product = await new ProductBuilder(fixture.Reference).CreateAsync(admin);

        AssertShape(await admin.Products.CreateAsync(new ProductBuilder(fixture.Reference).WithSku(product.Sku).Build()), HttpStatusCode.Conflict, "Conflict");
    }

    [Fact]
    public async Task A_409_for_insufficient_stock_has_the_shape_plus_the_numbers()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        var draft = await s.DraftIssueAsync(s.Admin, Warehouse, new Line(product.Id, 3));

        var response = await s.Admin.Documents.SubmitAsync(draft.Id);

        AssertShape(response, HttpStatusCode.Conflict, "Insufficient stock");
        Assert.Equal((product.Id, 0m, 3m), (response.Problem.ProductId, response.Problem.Available, response.Problem.Requested));
    }

    [Fact]
    public async Task An_unexpected_error_never_leaks_internals()
    {
        // No endpoint is meant to fail unexpectedly, so this checks the mapping on a hostile input instead: nothing in a 4xx body is a stack trace.
        var admin = await fixture.AsAsync(Roles.Admin);

        var response = await admin.PostRawJsonAsync("/api/products", "{\"sku\": \"x\", \"baseUomId\": \"not a number\"}");

        Assert.DoesNotContain("   at ", response.Body);
        Assert.DoesNotContain("System.", response.Body);
        Assert.DoesNotContain("Microsoft.", response.Body);
    }

    // ---------- correlation id ----------

    [Fact]
    public async Task Error_responses_carry_the_correlation_header_with_the_same_id_as_the_body()
    {
        var admin = await fixture.AsAsync(Roles.Admin);
        var product = await new ProductBuilder(fixture.Reference).CreateAsync(admin);

        var notFound = await admin.Documents.GetAsync(9_999_999);
        var conflict = await admin.Products.CreateAsync(new ProductBuilder(fixture.Reference).WithSku(product.Sku).Build());
        var badRequest = await admin.Products.CreateAsync(new ProductBuilder(fixture.Reference).WithSku("").Build());
        var unauthorized = await fixture.Anonymous().Auth.LoginAsync("nobody-" + Guid.NewGuid().ToString("N"), "x");

        foreach (var response in new[] { notFound, conflict, badRequest, unauthorized })
        {
            Assert.False(string.IsNullOrWhiteSpace(response.CorrelationHeader), $"HTTP {response.StatusCode} has no X-Correlation-Id header");
            Assert.Equal(response.CorrelationHeader, response.Problem.CorrelationId);
        }
    }

    [Fact]
    public async Task A_valid_correlation_id_is_echoed_on_success_and_on_errors_and_appears_in_the_body()
    {
        var admin = await fixture.AsAsync(Roles.Admin);
        const string id = "test-corr_ID-12345";

        var ok = await admin.SendAsync(HttpMethod.Get, "/api/warehouses", customize: r => r.Headers.Add("X-Correlation-Id", id));
        var notFound = await admin.SendAsync(HttpMethod.Get, "/api/documents/9999999", customize: r => r.Headers.Add("X-Correlation-Id", id));

        Assert.Equal(id, ok.ShouldBeOk().CorrelationHeader);
        Assert.Equal(id, notFound.CorrelationHeader);
        Assert.Equal(id, notFound.Problem.CorrelationId);
    }

    [Fact]
    public async Task A_missing_correlation_id_is_generated()
    {
        var admin = await fixture.AsAsync(Roles.Admin);

        var first = await admin.GetAsync("/api/warehouses");
        var second = await admin.GetAsync("/api/warehouses");

        Assert.False(string.IsNullOrWhiteSpace(first.CorrelationHeader));
        Assert.NotEqual(first.CorrelationHeader, second.CorrelationHeader);
    }

    [Theory]
    [InlineData("has spaces in it")]
    [InlineData("semi;colon")]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("0123456789012345678901234567890123456789012345678901234567890123456789")] // 70 characters, limit is 64
    public async Task An_unsafe_or_too_long_correlation_id_is_replaced_not_echoed(string supplied)
    {
        var admin = await fixture.AsAsync(Roles.Admin);

        var response = await admin.SendAsync(HttpMethod.Get, "/api/warehouses", customize: r => r.Headers.TryAddWithoutValidation("X-Correlation-Id", supplied));

        Assert.False(string.IsNullOrWhiteSpace(response.CorrelationHeader));
        Assert.NotEqual(supplied, response.CorrelationHeader);
        Assert.Matches("^[A-Za-z0-9_-]{1,64}$", response.CorrelationHeader!);
    }

    [Fact]
    public async Task The_correlation_id_is_also_on_responses_that_never_reach_a_controller()
    {
        var anonymous = fixture.Anonymous();

        var unauthorized = await anonymous.Warehouses.ListAsync();
        var health = await anonymous.GetAsync("/health");
        var unknownRoute = await anonymous.GetAsync("/api/no-such-route");

        Assert.False(string.IsNullOrWhiteSpace(unauthorized.CorrelationHeader));
        Assert.False(string.IsNullOrWhiteSpace(health.CorrelationHeader));
        Assert.False(string.IsNullOrWhiteSpace(unknownRoute.CorrelationHeader));
    }
}
