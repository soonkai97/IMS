using Microsoft.AspNetCore.HttpOverrides;
using Ims.Infrastructure;
using System.Threading.RateLimiting;
using Ims.Application;
using Ims.Domain;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
namespace Ims.Api;

/// <summary>Composition of the presentation layer: HTTP concerns only (auth, CORS, rate limits, serialisation, health, OpenAPI).</summary>
public static class PresentationServices
{
    public static IServiceCollection AddPresentation(this IServiceCollection services, IConfiguration cfg, IHostEnvironment env)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, CurrentUser>();
        services.AddCursorProtection(cfg, env);

        services.AddJwtAuthentication()
                .AddPermissionPolicies()
                .AddCorsPolicy(cfg, env)
                .AddAuthCookies(cfg)
                .AddTransportSecurity()
                .AddRateLimits(cfg)
                .AddRequestTimeoutPolicies()
                .AddForwardedHeaderSupport(cfg);

        services.AddWireJson();
        services.AddControllers().ConfigureApiBehaviorOptions(o => o.InvalidModelStateResponseFactory = ProblemResponses.FromModelState);
        services.AddOpenApiDocument();
        services.AddProblemDetails();
        services.AddExceptionHandler<ApiExceptionHandler>();
        services.AddHealthChecks().AddInfrastructureHealthChecks();
        return services;
    }

    /// <summary>JWT validation reads the same validated <see cref="JwtOptions"/> the token service signs with, so they cannot drift apart.</summary>
    private static IServiceCollection AddJwtAuthentication(this IServiceCollection services)
    {
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddSingleton<IConfigureOptions<JwtBearerOptions>, ConfigureJwtBearer>();
        return services;
    }

    /// <summary>
    /// One policy per permission of the catalog (named by its code) and the any-of policies of <see cref="AuthPolicies"/> (AUTH-02). A policy passes when the token carries
    /// the <c>perm</c> claim; the role claim is never read. A missing permission yields 403, no token (or a token of an old shape, see <see cref="ConfigureJwtBearer"/>) yields 401.
    /// </summary>
    private static IServiceCollection AddPermissionPolicies(this IServiceCollection services)
    {
        services.AddAuthorization(o =>
        {
            foreach (var code in PermissionCatalog.Codes) AddPolicy(o, code, [code]);
            foreach (var (name, codes) in AuthPolicies.AnyOf) AddPolicy(o, name, codes);
            o.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
        });
        return services;
    }

    private static void AddPolicy(AuthorizationOptions options, string name, IReadOnlyList<string> anyOf) =>
        options.AddPolicy(name, p => p.RequireAuthenticatedUser().RequireClaim(ClaimNames.Permission, anyOf));

    private static IServiceCollection AddCorsPolicy(this IServiceCollection services, IConfiguration cfg, IHostEnvironment env)
    {
        // Outside Development the allowed origins must be configured explicitly: a silent localhost default is not a production setting.
        var configured = cfg[ApiConfigKeys.CorsOrigins];
        if (string.IsNullOrWhiteSpace(configured) && !env.IsDevelopment() && !env.IsEnvironment(EnvironmentNames.Testing))
            throw new InvalidOperationException($"{ApiConfigKeys.CorsOrigins} must be configured.");
        var origins = (configured ?? ApiConfigKeys.DefaultCorsOrigin)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        // The refresh token travels in a cookie, which a cross-origin page can only send when the response allows credentials; browsers
        // refuse that combination with a wildcard origin, so a wildcard is rejected here instead of failing on the first request.
        if (origins.Any(origin => origin.Contains('*')))
            throw new InvalidOperationException($"{ApiConfigKeys.CorsOrigins} must list explicit origins; wildcards are not allowed.");
        services.AddSingleton(new AllowedOrigins(origins));
        services.AddCors(o => o.AddDefaultPolicy(p => p.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod().AllowCredentials()
            .WithExposedHeaders(HeaderNames.CorrelationId, HeaderNames.ContentDisposition, HeaderNames.ETag)));
        return services;
    }

    private static IServiceCollection AddRateLimits(this IServiceCollection services, IConfiguration cfg)
    {
        var configuredLimit = cfg.GetValue(ApiConfigKeys.RateLimitPerMinute, Limits.GlobalRateLimitPerMinute);
        var perMinute = configuredLimit > 0 ? configuredLimit : Limits.GlobalRateLimitPerMinute;
        var window = TimeSpan.FromMinutes(Limits.RateLimitWindowMinutes);
        services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.OnRejected = (context, ct) => new ValueTask(ProblemResponses.WriteRateLimitedAsync(
                context.HttpContext, context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter) ? retryAfter : null, ct));
            o.AddPolicy(RateLimitPolicies.Login, ctx => RateLimitPartition.GetFixedWindowLimiter(Caller(ctx),
                _ => new FixedWindowRateLimiterOptions { PermitLimit = Limits.LoginRateLimitPerMinute, Window = window }));
            o.AddPolicy(RateLimitPolicies.Refresh, ctx => RateLimitPartition.GetFixedWindowLimiter(Caller(ctx),
                _ => new FixedWindowRateLimiterOptions { PermitLimit = Limits.RefreshRateLimitPerMinute, Window = window }));
            o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx => RateLimitPartition.GetFixedWindowLimiter(
                ctx.User.Identity?.Name ?? Caller(ctx), _ => new FixedWindowRateLimiterOptions { PermitLimit = perMinute, Window = window }));
        });
        return services;
    }

    /// <summary>
    /// Behind a reverse proxy every request arrives from the proxy's address, which would put all users in one rate-limit bucket.
    /// Enable ForwardedHeaders:Enabled only when the API is reachable exclusively through a trusted proxy that sets X-Forwarded-For.
    /// </summary>
    private static IServiceCollection AddForwardedHeaderSupport(this IServiceCollection services, IConfiguration cfg)
    {
        if (!cfg.GetValue(ApiConfigKeys.ForwardedHeadersEnabled, false)) return services;
        services.Configure<ForwardedHeadersOptions>(o =>
        {
            o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            o.KnownIPNetworks.Clear();
            o.KnownProxies.Clear();
            // Only the configured proxies may set the client address; anyone else's X-Forwarded-For is ignored. Trusting every sender
            // would let a client rotate the header to dodge the per-IP rate limits.
            var proxies = (cfg[ApiConfigKeys.KnownProxies] ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (proxies.Length == 0) throw new InvalidOperationException($"{ApiConfigKeys.KnownProxies} must list the reverse proxy addresses when {ApiConfigKeys.ForwardedHeadersEnabled} is true.");
            foreach (var proxy in proxies) o.KnownProxies.Add(System.Net.IPAddress.Parse(proxy));
        });
        return services;
    }

    private static string Caller(HttpContext ctx) => ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}
