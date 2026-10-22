namespace Ims.Domain;

/// <summary>Entity names used in audit rows (and by clients filtering the audit log).</summary>
public static class AuditEntities
{
    public const string User = nameof(Ims.Domain.User);
    public const string Role = nameof(Ims.Domain.Role);
    public const string StockDocument = nameof(Ims.Domain.StockDocument);
    public const string PurchaseOrder = nameof(Ims.Domain.PurchaseOrder);
    public const string SystemSetting = nameof(Ims.Domain.SystemSetting);
    public const string Product = nameof(Ims.Domain.Product);
    public const string Attachment = nameof(Ims.Domain.Attachment);
    public const string Bulk = "bulk";
    public const string All = "all";
    public const string SystemActor = "system";
    public const string Anonymous = "anonymous";

    /// <summary>
    /// Every entity name an audit row can carry: the row-level audited master and security data, plus the business events (documents, files).
    /// The audit log filter matches a typed fragment against this list instead of scanning the table, and a client can offer it as a dropdown.
    /// </summary>
    public static readonly IReadOnlyList<string> Known =
    [
        nameof(Ims.Domain.ApprovalRule), nameof(Ims.Domain.Attachment), nameof(Ims.Domain.Category), nameof(Ims.Domain.Partner),
        nameof(Ims.Domain.Product), nameof(Ims.Domain.ProductBarcode), nameof(Ims.Domain.PurchaseOrder), nameof(Ims.Domain.Role), nameof(Ims.Domain.RolePermission), nameof(Ims.Domain.StockDocument),
        nameof(Ims.Domain.SystemSetting), nameof(Ims.Domain.Uom), nameof(Ims.Domain.UomConversion), nameof(Ims.Domain.User), nameof(Ims.Domain.Warehouse)
    ];

    /// <summary>
    /// The known entity names that contain <paramref name="fragment"/> (case-insensitive): "doc" finds StockDocument, "product" finds Product and
    /// ProductBarcode. A fragment that matches none is returned as is, so an exact name that is not in the list (an old row) still works.
    /// </summary>
    public static IReadOnlyList<string> Matching(string fragment)
    {
        var text = fragment.Trim();
        var matches = Known.Where(k => k.Contains(text, StringComparison.OrdinalIgnoreCase)).ToList();
        return matches.Count > 0 ? matches : [text];
    }
}
