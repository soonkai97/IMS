namespace Ims.Api;

/// <summary>
/// Authorization policies that are not one feature permission (those are named by the permission itself). A policy here is a door only: the
/// command still asks <c>DocumentActionPolicy</c> what the caller may do with the document.
/// </summary>
public static class AuthPolicies
{
    /// <summary>
    /// May cancel a document: someone who creates documents (cancels a Draft or a Submitted one) or someone who approves them (cancels an
    /// Approved one). The Owner approves but does not create, so the create permission alone would shut the Owner out.
    /// </summary>
    public const string DocumentCancel = "document.cancel";
}
