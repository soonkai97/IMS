using Ims.Domain;
namespace Ims.Api;

/// <summary>
/// The authorization policies beyond "one per catalog code" (a policy of that kind carries the code itself as its name). Each of these passes when the token
/// carries any one of the listed <c>perm</c> claims. The names are not permission codes, so a policy name can never be mistaken for one.
/// </summary>
public static class AuthPolicies
{
    /// <summary>Approve or reject a document: either approval tier (the Owner holds only the large one). The handler then checks the tier the document is routed to.</summary>
    public const string DocumentReview = "DocumentReview";

    /// <summary>Cancel a document: whoever may create one (cancels a Draft or Submitted document) or review one (cancels an Approved document). <c>DocumentActionPolicy.MayCancel</c> decides which.</summary>
    public const string DocumentCancel = "DocumentCancel";

    /// <summary>Cancel a purchase order: whoever may manage them (cancels a Draft or Submitted order) or review one (cancels an Approved order). <c>PurchaseOrderActionPolicy.MayCancel</c> decides which.</summary>
    public const string PurchaseOrderCancel = "PurchaseOrderCancel";

    /// <summary>The policies of this kind and the codes any one of which passes them.</summary>
    public static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> AnyOf = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
    {
        [DocumentReview] = [Permissions.DocumentApprove, Permissions.DocumentApproveLarge],
        [DocumentCancel] = [Permissions.DocumentCreate, Permissions.DocumentApprove, Permissions.DocumentApproveLarge],
        [PurchaseOrderCancel] = [Permissions.PoManage, Permissions.DocumentApprove, Permissions.DocumentApproveLarge],
    };
}
