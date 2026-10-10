using Ims.Api.Features.Auth;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Ims.Api;

/// <summary>Registers the refresh-token cookie, its CSRF guard and the <c>Auth</c> options, and marks the body copy of the token deprecated in OpenAPI.</summary>
public static class AuthCookieSetup
{
    public static IServiceCollection AddAuthCookies(this IServiceCollection services, IConfiguration cfg)
    {
        services.AddOptions<AuthOptions>().Bind(cfg.GetSection(ApiConfigKeys.AuthSection));
        services.AddSingleton<IRefreshTokenCookie, RefreshTokenCookie>();
        services.AddSingleton<ICsrfGuard, CsrfGuard>();
        services.Configure<OpenApiOptions>(OpenApiConstants.DocumentName, o => o.AddSchemaTransformer(MarkRefreshTokenDeprecated));
        return services;
    }

    /// <summary>Flags the <c>refreshToken</c> property of the request and the response as deprecated while both still exist.</summary>
    private static Task MarkRefreshTokenDeprecated(OpenApiSchema schema, OpenApiSchemaTransformerContext context, CancellationToken ct)
    {
        var type = context.JsonTypeInfo.Type;
        if (type != typeof(Ims.Application.Features.Auth.AuthResult) && type != typeof(Ims.Application.Features.Auth.RefreshRequest)) return Task.CompletedTask;
        if (schema.Properties is { } properties && properties.TryGetValue("refreshToken", out var property) && property is OpenApiSchema concrete)
        {
            concrete.Deprecated = true;
            concrete.Description = AuthDocs.RefreshTokenProperty;
        }
        return Task.CompletedTask;
    }
}
