using Ims.Domain;

namespace Ims.Api;

/// <summary>Kestrel limits that protect the server from slow or oversized clients (slowloris, huge uploads).</summary>
public static class ServerLimitsSetup
{
    public static WebApplicationBuilder ConfigureServerLimits(this WebApplicationBuilder builder)
    {
        builder.WebHost.ConfigureKestrel(o =>
        {
            o.AddServerHeader = false;
            o.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(Limits.KestrelHeadersTimeoutSeconds);
            o.Limits.KeepAliveTimeout = TimeSpan.FromSeconds(Limits.KeepAliveTimeoutSeconds);
            o.Limits.MaxRequestBodySize = Limits.MaxJsonBodyBytes; // uploads and imports raise it per endpoint
        });
        return builder;
    }
}
