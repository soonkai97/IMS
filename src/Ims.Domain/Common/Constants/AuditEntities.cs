namespace Ims.Domain;

/// <summary>Entity names used in audit rows (and by clients filtering the audit log).</summary>
public static class AuditEntities
{
    public const string User = nameof(Ims.Domain.User);
    public const string StockDocument = nameof(Ims.Domain.StockDocument);
    public const string SystemSetting = nameof(Ims.Domain.SystemSetting);
    public const string Product = nameof(Ims.Domain.Product);
    public const string Attachment = nameof(Ims.Domain.Attachment);
    public const string Bulk = "bulk";
    public const string All = "all";
    public const string SystemActor = "system";
    public const string Anonymous = "anonymous";
}
