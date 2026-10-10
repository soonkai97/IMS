using System.Net;
using Ims.Api.Tests.Builders;
using Ims.Api.Tests.Harness;
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
        var notFound = await admin.GetAsync("/api/documents/9999999");                             // exception handler
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

    [Fact]
    public async Task Sign_in_responses_are_never_cacheable_but_other_json_is_left_to_the_endpoint()
    {
        var accounts = new AccountFactory(fixture);
        var account = await accounts.CreateAsync(Roles.Clerk);
        var anonymous = fixture.Anonymous();

        var login = await anonymous.Auth.LoginAsync(account.Username, account.Password);
        var failedLogin = await anonymous.Auth.LoginAsync(account.Username, "wrong-password");
        var warehouses = await (await fixture.AsAsync(Roles.Admin)).Warehouses.ListAsync();

        Assert.Contains("no-store", login.Header("Cache-Control"));
        Assert.Contains("no-store", failedLogin.Header("Cache-Control")); // error responses add no-cache as well
        Assert.DoesNotContain("no-store", warehouses.Header("Cache-Control") ?? "");
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
