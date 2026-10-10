namespace Ims.Domain;

/// <summary>Figure 5 state machine. Rejection returns to Draft (reason kept on the document).</summary>
public enum DocumentStatus { Draft = 0, Submitted = 1, Approved = 2, Posted = 3, Reversed = 4, Cancelled = 5 }
