namespace Ims.Domain;

/// <summary>What a save did to one priced item of a price list.</summary>
public enum PriceChangeKind
{
    /// <summary>The item was not in the list and now is.</summary>
    Added,

    /// <summary>The item was in the list and no longer is.</summary>
    Removed,

    /// <summary>The item stays and its price is different.</summary>
    Repriced
}
