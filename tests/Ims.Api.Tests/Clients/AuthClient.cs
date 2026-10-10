using Ims.Api.Features.Auth;
using Ims.Api.Tests.Harness;
using Ims.Application.Features.Auth;

namespace Ims.Api.Tests.Clients;

public sealed class AuthClient(ApiClient api)
{
    public Task<ApiResponse> LoginAsync(string username, string password) =>
        api.PostAsync("/api/auth/login", new LoginRequest(username, password));

    public Task<ApiResponse> RefreshAsync(string? refreshToken) =>
        api.PostAsync("/api/auth/refresh", new RefreshRequest(refreshToken));

    public Task<ApiResponse> LogoutAsync(string? refreshToken) =>
        api.PostAsync("/api/auth/logout", new RefreshRequest(refreshToken));

    public Task<ApiResponse> MeAsync() => api.GetAsync("/api/auth/me");

    /// <summary>Refresh as a browser does it: no body token, the cookie, the custom header and an Origin. Each part can be left out or changed.</summary>
    public Task<ApiResponse> RefreshWithCookieAsync(string? cookieValue, BrowserRequest? browser = null) =>
        CookiePostAsync("/api/auth/refresh", cookieValue, browser ?? BrowserRequest.Trusted);

    public Task<ApiResponse> LogoutWithCookieAsync(string? cookieValue, BrowserRequest? browser = null) =>
        CookiePostAsync("/api/auth/logout", cookieValue, browser ?? BrowserRequest.Trusted);

    private Task<ApiResponse> CookiePostAsync(string url, string? cookieValue, BrowserRequest browser) =>
        api.SendAsync(HttpMethod.Post, url, customize: request =>
        {
            request.Content = new StringContent("", System.Text.Encoding.UTF8, "application/json");
            if (cookieValue is not null) request.Headers.Add("Cookie", $"{CookieName}={Uri.EscapeDataString(cookieValue)}");
            if (browser.Origin is not null) request.Headers.Add("Origin", browser.Origin);
            if (browser.Referer is not null) request.Headers.Add("Referer", browser.Referer);
            if (browser.RequestedWith is not null) request.Headers.Add(AuthCookie.CsrfHeaderName, browser.RequestedWith);
        });

    /// <summary>The cookie name in the Testing environment (anything but Development is Secure, so it carries the prefix).</summary>
    public static string CookieName => AuthCookie.RefreshName(secure: true);
}
