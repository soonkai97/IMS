namespace Ims.Api;

public static class ApiConfigKeys
{
    public const string CorsOrigins = "Cors:Origins";
    public const string DefaultCorsOrigin = "http://localhost:4200";
    public const string RateLimitPerMinute = "RateLimit:PerMinute";
    public const string LogDirectory = "Logging:Directory";
    public const string DefaultLogDirectory = "logs";
    public const string MigrateOnStartup = "Database:MigrateOnStartup";
    public const string ForwardedHeadersEnabled = "ForwardedHeaders:Enabled";
    public const string KnownProxies = "ForwardedHeaders:KnownProxies";
    public const string AuthSection = "Auth";
    public const string DataProtectionKeysPath = "DataProtection:KeysPath";
    public const string DataProtectionApplicationName = "Ims";
    public const string DefaultDataProtectionKeysFolder = "ims-dataprotection-keys";
}
