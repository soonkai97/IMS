namespace Ims.Domain;

/// <summary>
/// Who may satisfy an approval rule: a holder of <see cref="Permissions.DocumentApprove"/> (Standard) or of <see cref="Permissions.DocumentApproveLarge"/> (Large).
/// Two levels by design; a third is a new permission and a migration. The values are what the rule stores once it keeps a tier (ADR roles-permissions 4.1).
/// </summary>
public enum ApproverTier
{
    Standard = 1,
    Large = 2
}
