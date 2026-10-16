namespace Ims.Domain;

/// <summary>
/// Who is acting and what they may do: what the Domain policies (<see cref="DocumentActionPolicy"/>, <see cref="ApprovalPolicy"/>,
/// <see cref="PostingAuthority"/>) are asked about. A policy never sees a role name, only the permissions the person holds.
/// </summary>
public sealed record ActorRights(int UserId, EffectivePermissions Effective)
{
    public bool Has(string permission) => Effective.Has(permission);
}
