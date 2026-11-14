namespace Ims.Domain;

/// <summary>Why a caller may not approve or reject a document held for approval (<see cref="ApprovalPolicy.ReviewRefusal"/>). Application turns it into the sentence and the 403.</summary>
public enum ReviewRefusalReason
{
    /// <summary>The caller may review it.</summary>
    None,

    /// <summary>The caller holds only the large tier and the document is not a large one: a supervisor reviews it.</summary>
    OwnerReviewsLargeOnly,

    /// <summary>The document is above the caller's approval limit: routed to the other tier, or a Large rule in force now needs the large tier.</summary>
    AboveApprovalLimit,
}
