namespace Ims.Api;

/// <summary>Keys of <c>HttpContext.Items</c>: what one request carries between the pipeline and the code that serves it.</summary>
public static class HttpItemKeys
{
    /// <summary>The caller's <see cref="Ims.Domain.AccessSnapshot"/>, put there by the bearer event once the token is valid.</summary>
    public const string AccessSnapshot = "ims.access-snapshot";
}
