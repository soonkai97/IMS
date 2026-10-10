namespace Ims.Api;

public static class LogTemplates
{
    public static readonly string File = $"{AppIdentity.Slug}-.log";
    public const string Console = "[{Timestamp:HH:mm:ss} {Level:u3}] {CorrelationId} {Message:lj} {Properties:j}{NewLine}{Exception}";
    public const string FileLine = "{Timestamp:o} [{Level:u3}] {CorrelationId} {Message:lj} {Properties:j}{NewLine}{Exception}";
    public const string Request = "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0} ms";
    public const int RetainedFiles = 30;
    public static readonly string ApplicationName = AppIdentity.AssemblyName;
    public const string CorrelationProperty = "CorrelationId";
}
