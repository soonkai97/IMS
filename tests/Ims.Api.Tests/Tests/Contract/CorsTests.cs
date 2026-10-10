using System.Net;
using Ims.Api.Tests.Builders;
using Ims.Api.Tests.Harness;
using Ims.Domain;

namespace Ims.Api.Tests.Tests.Contract;

/// <summary>The browser only shows an error body to the page if the error response carries the CORS headers too.</summary>
[Collection(ApiCollection.Name)]
public class CorsTests(ApiFixture fixture)
{
    private const string AllowedOrigin = "http://localhost:4200"; // Cors:Origins in appsettings.json
    private const string AllowOriginHeader = "Access-Control-Allow-Origin";

    private static Action<HttpRequestMessage> From(string origin) => r => r.Headers.Add("Origin", origin);

    [Fact]
    public async Task A_success_response_for_the_allowed_origin_carries_the_cors_headers_and_exposes_the_correlation_id()
    {
        var admin = await fixture.AsAsync(Roles.Admin);

        var response = await admin.SendAsync(HttpMethod.Get, "/api/warehouses", customize: From(AllowedOrigin));

        response.ShouldBeOk();
        Assert.Equal(AllowedOrigin, response.Header(AllowOriginHeader));
        Assert.Contains("X-Correlation-Id", response.Header("Access-Control-Expose-Headers"));
    }

    [Fact]
    public async Task A_400_response_carries_the_cors_headers()
    {
        var admin = await fixture.AsAsync(Roles.Admin);

        var response = await admin.SendAsync(HttpMethod.Post, "/api/categories", new { name = "" }, From(AllowedOrigin));

        response.ShouldBe(HttpStatusCode.BadRequest);
        Assert.Equal(AllowedOrigin, response.Header(AllowOriginHeader));
    }

    [Fact]
    public async Task A_409_response_carries_the_cors_headers()
    {
        var admin = await fixture.AsAsync(Roles.Admin);
        var product = await new ProductBuilder(fixture.Reference).CreateAsync(admin);

        var response = await admin.SendAsync(HttpMethod.Post, "/api/products", new ProductBuilder(fixture.Reference).WithSku(product.Sku).Build(), From(AllowedOrigin));

        response.ShouldBe(HttpStatusCode.Conflict);
        Assert.Equal(AllowedOrigin, response.Header(AllowOriginHeader));
    }

    [Fact]
    public async Task A_404_response_carries_the_cors_headers()
    {
        var admin = await fixture.AsAsync(Roles.Admin);

        var response = await admin.SendAsync(HttpMethod.Get, "/api/documents/9999999", customize: From(AllowedOrigin));

        response.ShouldBe(HttpStatusCode.NotFound);
        Assert.Equal(AllowedOrigin, response.Header(AllowOriginHeader));
    }

    [Fact]
    public async Task A_401_and_a_403_response_carry_the_cors_headers()
    {
        var clerk = await fixture.AsAsync(Roles.Clerk);

        var unauthorized = await fixture.Anonymous().SendAsync(HttpMethod.Get, "/api/warehouses", customize: From(AllowedOrigin));
        var forbidden = await clerk.SendAsync(HttpMethod.Get, "/api/audit", customize: From(AllowedOrigin));

        unauthorized.ShouldBe(HttpStatusCode.Unauthorized);
        forbidden.ShouldBe(HttpStatusCode.Forbidden);
        Assert.Equal(AllowedOrigin, unauthorized.Header(AllowOriginHeader));
        Assert.Equal(AllowedOrigin, forbidden.Header(AllowOriginHeader));
    }

    [Fact]
    public async Task A_preflight_request_from_the_allowed_origin_is_answered_without_a_token()
    {
        var response = await fixture.Anonymous().SendAsync(HttpMethod.Options, "/api/documents/receipts", customize: r =>
        {
            r.Headers.Add("Origin", AllowedOrigin);
            r.Headers.Add("Access-Control-Request-Method", "POST");
            r.Headers.Add("Access-Control-Request-Headers", "authorization,content-type");
        });

        Assert.True(response.IsSuccess, $"Preflight got {response.StatusCode}");
        Assert.Equal(AllowedOrigin, response.Header(AllowOriginHeader));
        Assert.Contains("POST", response.Header("Access-Control-Allow-Methods"));
    }

    [Fact]
    public async Task Another_origin_gets_no_cors_headers()
    {
        var admin = await fixture.AsAsync(Roles.Admin);

        var response = await admin.SendAsync(HttpMethod.Get, "/api/warehouses", customize: From("https://evil.example"));

        Assert.Null(response.Header(AllowOriginHeader));
    }
}
