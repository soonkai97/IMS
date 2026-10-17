namespace Ims.Domain;

/// <summary>The claims of an access token beyond the standard ones (sub, name, role). Built from one access snapshot at sign-in and at refresh (ADR roles-permissions 3.2).</summary>
public static class ClaimNames
{
    /// <summary>One claim per warehouse the user is assigned to; left out when the user sees every warehouse (<see cref="AllWarehouses"/>).</summary>
    public const string Warehouse = "wh";

    /// <summary>One claim per permission code the user's role holds.</summary>
    public const string Permission = "perm";

    /// <summary>"true" or "false": the user sees every warehouse, whatever the list of <see cref="Warehouse"/> claims says.</summary>
    public const string AllWarehouses = "allwh";

    /// <summary>The shape of the claims above. It moves when the shape changes, so a later release can refuse tokens of the old shape.</summary>
    public const string ClaimsVersion = "acv";

    /// <summary>The value of <see cref="ClaimsVersion"/> this release writes.</summary>
    public const int CurrentClaimsVersion = 1;
}
