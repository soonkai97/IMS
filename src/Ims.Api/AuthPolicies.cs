namespace Ims.Api;

/// <summary>Authorization policies that are not one feature permission. A policy here is a door only: the command still asks <c>DocumentActionPolicy</c>.</summary>
public static class AuthPolicies
{
    /// <summary>Create right (cancels a Draft or Submitted document) OR approve right (cancels an Approved one): the Owner approves but cannot create.</summary>
    public const string DocumentCancel = "document.cancel";
}
