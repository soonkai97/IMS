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
    public const string DocumentRecalled = "document.recalled";
    public const string DocumentCancelled = "document.cancelled";
    public const string DocumentPosted = "document.posted";
    public const string DocumentReversed = "document.reversed";

    public const string AttachmentUploaded = "attachment.uploaded";
    public const string AttachmentDeleted = "attachment.deleted";

    public const string ImportProducts = "import.products";
    public const string ImportOpeningStock = "import.opening-stock";

    /// <summary>Written once by the roles migration (system actor) when rules that only an Administrator could satisfy became Large and so can now be satisfied by the Owner too.</summary>
    public const string ApprovalRulesWidened = "approval-rules.widened";

    /// <summary>The sign-in events: what the audit log leaves out when asked to hide sign-ins.</summary>
    public static readonly IReadOnlyList<string> SignIns = [LoginSuccess, LoginFailed, LoginLocked, LoginLockout, Logout];

    /// <summary>Every action name above, for the audit log filter dropdown.</summary>
    public static readonly IReadOnlyList<string> Known =
    [
        RowCreated, RowUpdated, RowDeleted, LoginSuccess, LoginFailed, LoginLocked, LoginLockout, Logout, UserCreated, UserUpdated, PasswordChanged,
        SettingsUpdated, DocumentCreated, DocumentSubmitted, DocumentApproved, DocumentRejected, DocumentRecalled, DocumentCancelled, DocumentPosted, DocumentReversed,
        AttachmentUploaded, AttachmentDeleted, ImportProducts, ImportOpeningStock, ApprovalRulesWidened
    ];
}
