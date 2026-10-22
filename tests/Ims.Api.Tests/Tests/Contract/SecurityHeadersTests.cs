using System.Net;
using Ims.Api.Tests.Builders;
using Ims.Api.Tests.Harness;
using Ims.Application.Features.Documents;
using Ims.Domain;

namespace Ims.Api.Tests.Tests.Contract;

/// <summary>Every API response, whatever its status, carries the same defensive headers (SEC-24); these tests pin the exact values.</summary>
[Collection(ApiCollection.Name)]
public class SecurityHeadersTests(ApiFixture fixture)
{
    private static void AssertSecurityHeaders(ApiResponse response)
    {
        Assert.Equal("nosniff", response.Header("X-Content-Type-Options"));
        Assert.Equal("no-referrer", response.Header("Referrer-Policy"));
        Assert.Equal("same-site", response.Header("Cross-Origin-Resource-Policy"));
        Assert.Equal("DENY", response.Header("X-Frame-Options"));
        Assert.Equal("default-src 'none'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'", response.Header("Content-Security-Policy"));

        var permissions = response.Header("Permissions-Policy")!;
        foreach (var feature in new[] { "camera", "microphone", "geolocation", "payment", "usb" })
            Assert.Contains($"{feature}=()", permissions);

        Assert.Null(response.Header("Server"));
        Assert.Null(response.Header("X-Powered-By"));
    }

    [Fact]
    public async Task A_success_response_carries_the_headers()
    {
        var admin = await fixture.AsAsync(Roles.Admin);

        var response = await admin.Warehouses.ListAsync();

        response.ShouldBeOk();
        AssertSecurityHeaders(response);
    }

    [Fact]
    public async Task Error_responses_from_every_layer_carry_the_headers()
    {
        var admin = await fixture.AsAsync(Roles.Admin);
        var clerk = await fixture.AsAsync(Roles.Clerk);

        var unauthorized = await fixture.Anonymous().Warehouses.ListAsync();                       // authentication middleware
        var forbidden = await clerk.GetAsync("/api/audit");                                        // authorization middleware
        var notFound = await admin.GetAsync($"/api/documents/{Guid.NewGuid()}");                             // exception handler
        var noRoute = await admin.GetAsync("/api/no-such-route");                                  // status-code pages
        var badRequest = await admin.PostAsync("/api/categories", new { name = "" });              // validation
        var malformed = await admin.PostRawJsonAsync("/api/categories", "{not json");              // model binding

        unauthorized.ShouldBe(HttpStatusCode.Unauthorized);
        forbidden.ShouldBe(HttpStatusCode.Forbidden);
        notFound.ShouldBe(HttpStatusCode.NotFound);
        noRoute.ShouldBe(HttpStatusCode.NotFound);
        badRequest.ShouldBe(HttpStatusCode.BadRequest);
        malformed.ShouldBe(HttpStatusCode.BadRequest);
        foreach (var response in new[] { unauthorized, forbidden, notFound, noRoute, badRequest, malformed }) AssertSecurityHeaders(response);
    }

    [Fact]
    public async Task A_rate_limited_response_carries_the_headers()
    {
        var client = fixture.Anonymous();
        ApiResponse? limited = null;
        for (var attempt = 0; attempt < Limits.LoginRateLimitPerMinute + 1 && limited is null; attempt++)
        {
            var response = await client.Auth.LoginAsync("nobody", "wrong");
            if (response.Status == HttpStatusCode.TooManyRequests) limited = response;
        }

        Assert.NotNull(limited);
        AssertSecurityHeaders(limited);
    }

    /// <summary>Security retest F-3 (API8, ASVS 8.2): every API response (cost data included) is private and never stored; pages that are not the API are left alone.</summary>
    [Fact]
    public async Task Every_api_response_is_private_and_no_store_but_pages_outside_the_api_are_left_alone()
    {
        var account = await new AccountFactory(fixture).CreateAsync(Roles.Clerk);
        var anonymous = fixture.Anonymous();
        var admin = await fixture.AsAsync(Roles.Admin);
        var product = await new ProductBuilder(fixture.Reference).CreateAsync(admin);
        var document = (await admin.Documents.CreateReceiptAsync(new DocumentSaveRequest(fixture.Reference.DefaultWarehouseId, fixture.Reference.SupplierId, null, null,
            [new DocumentLineInput(product.Id, 2, null, 3m, null)]))).Data<DocumentDto>();

        var success = new[]
        {
            await anonymous.Auth.LoginAsync(account.Username, account.Password), await admin.Warehouses.ListAsync(), await admin.Products.ListAsync(),
            await admin.Documents.ListAsync(), await admin.Documents.GetAsync(document.Id)
        };
        var errors = new[]
        {
            await anonymous.Auth.LoginAsync(account.Username, "wrong-password"), await anonymous.GetAsync("/api/purchase-orders"), await admin.GetAsync("/api/no-such-route"),
            await admin.Documents.GetAsync(Guid.NewGuid()), await admin.PostRawJsonAsync("/api/categories", "{ not json")
        };

        foreach (var response in success) Assert.Equal(["no-store", "private"], response.ShouldBeOk().CacheDirectives);
        foreach (var response in errors) Assert.Contains("no-store", response.Header("Cache-Control")); // an error answer adds no-cache as well
        Assert.DoesNotContain("no-store", (await anonymous.GetAsync("/openapi/v1.json")).Header("Cache-Control") ?? "");
        Assert.DoesNotContain("no-store", (await anonymous.GetAsync("/swagger/index.html")).Header("Cache-Control") ?? "");
    }

    [Fact]
    public async Task The_swagger_page_keeps_working_because_it_is_exempt_from_the_json_only_csp_but_still_gets_the_other_headers()
    {
        var ui = await fixture.Anonymous().GetAsync("/swagger/index.html");
        var document = await fixture.Anonymous().GetAsync("/openapi/v1.json");

        ui.ShouldBeOk();
        Assert.Null(ui.Header("Content-Security-Policy"));
        Assert.Equal("nosniff", ui.Header("X-Content-Type-Options"));
        AssertSecurityHeaders(document.ShouldBeOk());
    }

    [Fact]
    public async Task The_openapi_document_describes_the_cookie_flow_and_marks_the_body_token_deprecated()
    {
        var document = (await fixture.Anonymous().GetAsync("/openapi/v1.json")).ShouldBeOk().Json().RootElement;

        var refresh = document.GetProperty("paths").GetProperty("/api/auth/refresh").GetProperty("post").GetProperty("description").GetString()!;
        Assert.Contains("HttpOnly", refresh);
        Assert.Contains("X-Requested-With", refresh);
        Assert.Contains("DEPRECATED", refresh);

        var schemas = document.GetProperty("components").GetProperty("schemas");
        foreach (var name in new[] { "AuthResult", "RefreshRequest" })
        {
            var property = schemas.GetProperty(name).GetProperty("properties").GetProperty("refreshToken");
            Assert.True(property.GetProperty("deprecated").GetBoolean(), $"{name}.refreshToken is not marked deprecated.");
        }
    }
}
