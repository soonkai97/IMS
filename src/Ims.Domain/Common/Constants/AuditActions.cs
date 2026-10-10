namespace Ims.Domain;

/// <summary>Business-event names written to the audit log. Row-level changes use "created" / "updated" / "deleted".</summary>
public static class AuditActions
{
    public const string RowCreated = "created";
    public const string RowUpdated = "updated";
    public const string RowDeleted = "deleted";

    public const string LoginSuccess = "login.success";
    public const string LoginFailed = "login.failed";
    public const string LoginLocked = "login.locked";
    public const string LoginLockout = "login.lockout";
    public const string Logout = "logout";

    public const string UserCreated = "user.created";
    public const string UserUpdated = "user.updated";
    public const string PasswordChanged = "user.password-changed";
    public const string SettingsUpdated = "settings.updated";

    public const string DocumentCreated = "document.created";
    public const string DocumentSubmitted = "document.submitted";
    public const string DocumentApproved = "document.approved";
    public const string DocumentRejected = "document.rejected";
    public const string DocumentCancelled = "document.cancelled";
    public const string DocumentPosted = "document.posted";
    public const string DocumentReversed = "document.reversed";

    public const string AttachmentUploaded = "attachment.uploaded";
    public const string AttachmentDeleted = "attachment.deleted";

    public const string ImportProducts = "import.products";
    public const string ImportOpeningStock = "import.opening-stock";
}
