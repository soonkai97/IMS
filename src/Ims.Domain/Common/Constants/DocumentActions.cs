namespace Ims.Domain;

/// <summary>Action names returned to the UI in <c>allowedActions</c>. The UI shows a button only if its action is listed.</summary>
public static class DocumentActions
{
    public const string Edit = "edit";
    public const string Submit = "submit";
    public const string Approve = "approve";
    public const string Reject = "reject";
    public const string Cancel = "cancel";
    public const string Post = "post";
    public const string Reverse = "reverse";
}
